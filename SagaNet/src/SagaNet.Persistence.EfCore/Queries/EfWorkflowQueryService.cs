using Microsoft.EntityFrameworkCore;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;
using SagaNet.Persistence.EfCore.Entities;
using System.Linq;

namespace SagaNet.Persistence.EfCore.Queries;

/// <summary>
/// EF Core implementation of <see cref="IWorkflowQueryService"/>.
/// Joins the DB with the in-memory <see cref="IWorkflowRegistry"/> to enrich step
/// data with descriptions from the workflow definition.
/// </summary>
public sealed class EfWorkflowQueryService : IWorkflowQueryService
{
    private readonly SagaNetDbContext _db;
    private readonly IWorkflowRegistry _registry;

    public EfWorkflowQueryService(SagaNetDbContext db, IWorkflowRegistry registry)
    {
        _db = db;
        _registry = registry;
    }

    public async Task<WorkflowProgress?> GetProgressAsync(
        Guid instanceId,
        CancellationToken ct = default)
    {
        var entity = await _db.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.ExecutionPointers)
            .FirstOrDefaultAsync(x => x.Id == instanceId, ct);

        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<WorkflowProgress>> GetProgressByCorrelationIdAsync(
        string correlationId,
        CancellationToken ct = default)
    {
        var entities = await _db.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.ExecutionPointers)
            .Where(x => x.CorrelationId == correlationId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<WorkflowProgress>> GetRecentAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        var entities = await _db.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.ExecutionPointers)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowProgress>> GetStuckAtStepAsync(
        string stepName,
        StepQueryFilter? filter = null,
        CancellationToken ct = default)
    {
        // "Stuck at step" means the workflow has an execution pointer for that step
        // whose status is Pending, Running, or WaitingForEvent.
        var activeStatuses = new[]
        {
            (int)StepStatus.Pending,
            (int)StepStatus.Running,
            (int)StepStatus.WaitingForEvent
        };

        var query = _db.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.ExecutionPointers)
            .Where(x => x.ExecutionPointers.Any(p =>
                p.StepName == stepName &&
                activeStatuses.Contains(p.Status)));

        query = ApplyFilter(query, filter);

        var entities = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(filter?.Limit ?? 50)
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowProgress>> GetFailedAtStepAsync(
        string stepName,
        StepQueryFilter? filter = null,
        CancellationToken ct = default)
    {
        // "Failed at step" means the workflow has an execution pointer for that step
        // whose status is Failed (all retries exhausted, compensation triggered or not).
        var query = _db.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.ExecutionPointers)
            .Where(x => x.ExecutionPointers.Any(p =>
                p.StepName == stepName &&
                p.Status == (int)StepStatus.Failed));

        query = ApplyFilter(query, filter);

        var entities = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(filter?.Limit ?? 50)
            .ToListAsync(ct);

        return entities.Select(Map).ToList();
    }

    // ── Shared filter helper ──────────────────────────────────────────────────

    private static IQueryable<Entities.WorkflowInstanceEntity> ApplyFilter(
        IQueryable<Entities.WorkflowInstanceEntity> query,
        StepQueryFilter? filter)
    {
        if (filter is null) return query;

        if (filter.WorkflowName is not null)
            query = query.Where(x => x.WorkflowName == filter.WorkflowName);

        if (filter.CreatedAfter is not null)
            query = query.Where(x => x.CreatedAt >= filter.CreatedAfter.Value);

        if (filter.CreatedBefore is not null)
            query = query.Where(x => x.CreatedAt < filter.CreatedBefore.Value);

        return query;
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private WorkflowProgress Map(WorkflowInstanceEntity entity)
    {
        var definition = _registry.Find(entity.WorkflowName, entity.Version);

        // Forward-flow steps from definition (descriptions, ordering, full step list).
        var forwardDefs = definition?.Steps
            .Where(s => !s.IsCompensationStep)
            .OrderBy(s => s.Index)
            .ToList();

        // All compensation step defs (for labelling compensation pointers).
        var compDefs = definition?.Steps
            .Where(s => s.IsCompensationStep)
            .ToDictionary(s => s.Index);

        // Execution pointers indexed by step index.
        // GroupBy rather than ToDictionary so that duplicate StepIndex entries (which
        // should not occur in normal flow but can if the same instance was somehow
        // processed concurrently) don't throw — we take the most recent pointer.
        var pointers = entity.ExecutionPointers
            .GroupBy(p => p.StepIndex)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.StartTime ?? DateTime.MinValue).First());

        var steps = new List<StepProgress>();

        if (forwardDefs is { Count: > 0 })
        {
            // Add all defined forward steps, merging in execution data if it exists.
            foreach (var def in forwardDefs)
            {
                pointers.TryGetValue(def.Index, out var pointer);
                steps.Add(MapForwardStep(def.Name, def.Description, pointer));
            }

            // If compensation ran, show those steps appended after the forward list.
            foreach (var pointer in entity.ExecutionPointers
                         .Where(p => p.Status is (int)StepStatus.Compensating
                                               or (int)StepStatus.Compensated
                                               or (int)StepStatus.Failed
                                  && compDefs is not null && compDefs.ContainsKey(p.StepIndex))
                         .OrderBy(p => p.StepIndex))
            {
                compDefs!.TryGetValue(pointer.StepIndex, out var compDef);
                steps.Add(MapCompensationStep(compDef?.Name ?? pointer.StepName, pointer));
            }
        }
        else
        {
            // No definition registered — fall back to showing execution pointers as-is.
            foreach (var pointer in entity.ExecutionPointers.OrderBy(p => p.StepIndex))
                steps.Add(MapForwardStep(pointer.StepName, null, pointer));
        }

        // Progress calculation — only forward steps count toward the percentage.
        var forwardSteps = steps.Where(s => !s.IsCompensation).ToList();
        var totalSteps = forwardSteps.Count;
        var completedSteps = forwardSteps.Count(s => s.Status == StepStatus.Complete);
        var status = (WorkflowStatus)entity.Status;

        var percent = status == WorkflowStatus.Complete
            ? 100
            : totalSteps == 0
                ? 0
                : (int)Math.Floor(completedSteps * 100.0 / totalSteps);

        return new WorkflowProgress
        {
            InstanceId = entity.Id,
            WorkflowName = entity.WorkflowName,
            Version = entity.Version,
            Status = status,
            StatusLabel = BuildStatusLabel(status, steps),
            ProgressPercent = percent,
            TotalSteps = totalSteps,
            CompletedSteps = completedSteps,
            StartedAt = entity.CreatedAt,
            CompletedAt = entity.CompleteAt,
            Error = entity.Error,
            CorrelationId = entity.CorrelationId,
            Steps = steps
        };
    }

    private static StepProgress MapForwardStep(
        string name,
        string? description,
        ExecutionPointerEntity? pointer)
    {
        if (pointer is null)
        {
            // Step hasn't started yet — show as Pending.
            return new StepProgress
            {
                Name = name,
                Description = description,
                Status = StepStatus.Pending,
                StatusLabel = "Pending",
                IsCompensation = false
            };
        }

        var status = (StepStatus)pointer.Status;
        return new StepProgress
        {
            Name = name,
            Description = description,
            Status = status,
            StatusLabel = StepStatusLabel(status),
            AttemptCount = pointer.AttemptCount,
            StartedAt = pointer.StartTime,
            CompletedAt = pointer.EndTime,
            DurationMs = pointer is { StartTime: not null, EndTime: not null }
                ? (pointer.EndTime.Value - pointer.StartTime.Value).TotalMilliseconds
                : null,
            Error = pointer.Error,
            IsCompensation = false
        };
    }

    private static StepProgress MapCompensationStep(string name, ExecutionPointerEntity pointer)
    {
        var status = (StepStatus)pointer.Status;
        return new StepProgress
        {
            Name = name,
            Description = "Rollback / compensation step",
            Status = status,
            StatusLabel = StepStatusLabel(status),
            AttemptCount = pointer.AttemptCount,
            StartedAt = pointer.StartTime,
            CompletedAt = pointer.EndTime,
            DurationMs = pointer is { StartTime: not null, EndTime: not null }
                ? (pointer.EndTime.Value - pointer.StartTime.Value).TotalMilliseconds
                : null,
            Error = pointer.Error,
            IsCompensation = true
        };
    }

    private static string BuildStatusLabel(WorkflowStatus status, List<StepProgress> steps)
        => status switch
        {
            WorkflowStatus.Complete    => "Completed successfully",
            WorkflowStatus.Failed      => "Failed — manual intervention required",
            WorkflowStatus.Compensated => "Rolled back — all changes undone",
            WorkflowStatus.Terminated  => "Terminated",
            WorkflowStatus.Suspended   => BuildSuspendedLabel(steps),
            WorkflowStatus.Runnable or
            WorkflowStatus.Running     => BuildRunningLabel(steps),
            _                          => status.ToString()
        };

    private static string BuildRunningLabel(List<StepProgress> steps)
    {
        var active = steps.FirstOrDefault(s =>
            s.Status is StepStatus.Running or StepStatus.Pending && !s.IsCompensation);
        return active is not null ? $"Running: {active.Name}" : "Running";
    }

    private static string BuildSuspendedLabel(List<StepProgress> steps)
    {
        var waiting = steps.FirstOrDefault(s => s.Status == StepStatus.WaitingForEvent);
        return waiting is not null
            ? $"Waiting for external event at: {waiting.Name}"
            : "Suspended";
    }

    private static string StepStatusLabel(StepStatus status) => status switch
    {
        StepStatus.Pending          => "Pending",
        StepStatus.Running          => "Running",
        StepStatus.Complete         => "Completed",
        StepStatus.Failed           => "Failed",
        StepStatus.Compensating     => "Rolling back",
        StepStatus.Compensated      => "Rolled back",
        StepStatus.WaitingForEvent  => "Waiting for event",
        StepStatus.Skipped          => "Skipped",
        _                           => status.ToString()
    };
}
