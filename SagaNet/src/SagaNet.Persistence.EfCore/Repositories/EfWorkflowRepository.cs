using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;
using SagaNet.Persistence.EfCore.Entities;

namespace SagaNet.Persistence.EfCore.Repositories;

/// <summary>
/// EF Core + SQL Server implementation of <see cref="IWorkflowRepository"/>.
/// Uses optimistic concurrency (<c>RowVersion</c>) to prevent two hosts from
/// executing the same workflow instance simultaneously.
/// </summary>
public sealed class EfWorkflowRepository : IWorkflowRepository
{
    private readonly SagaNetDbContext _db;
    private readonly ILogger<EfWorkflowRepository> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public EfWorkflowRepository(SagaNetDbContext db, ILogger<EfWorkflowRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<WorkflowInstance> CreateInstanceAsync(
        WorkflowInstance instance,
        CancellationToken ct = default)
    {
        var entity = ToEntity(instance);
        _db.WorkflowInstances.Add(entity);
        await _db.SaveChangesAsync(ct);
        instance.RowVersion = entity.RowVersion;
        return instance;
    }

    public async Task<WorkflowInstance?> GetInstanceAsync(
        Guid instanceId,
        CancellationToken ct = default)
    {
        var entity = await _db.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.ExecutionPointers)
            .FirstOrDefaultAsync(x => x.Id == instanceId, ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<WorkflowInstance?> TryAcquireInstanceAsync(
        Guid instanceId,
        CancellationToken ct = default)
    {
        // Load with tracking so EF Core can detect the RowVersion concurrency conflict.
        var entity = await _db.WorkflowInstances
            .Include(x => x.ExecutionPointers)
            .FirstOrDefaultAsync(x => x.Id == instanceId, ct);

        if (entity is null) return null;

        if ((WorkflowStatus)entity.Status != WorkflowStatus.Runnable)
            return null;

        entity.Status = (int)WorkflowStatus.Running;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another host claimed it first.
            return null;
        }

        return ToDomain(entity);
    }

    public async Task UpdateInstanceAsync(
        WorkflowInstance instance,
        CancellationToken ct = default)
    {
        var entity = await _db.WorkflowInstances
            .Include(x => x.ExecutionPointers)
            .FirstOrDefaultAsync(x => x.Id == instance.Id, ct);

        if (entity is null)
        {
            _logger.LogWarning("Attempted to update non-existent instance [{InstanceId}]", instance.Id);
            return;
        }

        MapToEntity(instance, entity);

        try
        {
            await _db.SaveChangesAsync(ct);
            instance.RowVersion = entity.RowVersion;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex,
                "Concurrency conflict updating instance [{InstanceId}] — instance may have been modified externally.",
                instance.Id);
            throw;
        }
    }

    public async Task<IReadOnlyList<Guid>> GetRunnableInstanceIdsAsync(
        int batchSize,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.WorkflowInstances
            .AsNoTracking()
            .Where(x => x.Status == (int)WorkflowStatus.Runnable &&
                        (x.NextExecutionTime == null || x.NextExecutionTime <= now))
            .OrderBy(x => x.NextExecutionTime)
            .Take(batchSize)
            .Select(x => x.Id)
            .ToListAsync(ct);
    }

    public async Task PublishEventAsync(
        WorkflowEvent @event,
        CancellationToken ct = default)
    {
        _db.WorkflowEvents.Add(new WorkflowEventEntity
        {
            Id = @event.Id,
            EventName = @event.EventName,
            EventKey = @event.EventKey,
            EventDataJson = @event.EventDataJson,
            CreatedAt = @event.CreatedAt,
            IsConsumed = false
        });

        // Wake any waiting workflow instances for this event.
        var waitingPointers = await _db.ExecutionPointers
            .Include(p => p.WorkflowInstance)
            .Where(p => p.Status == (int)StepStatus.WaitingForEvent &&
                        p.EventName == @event.EventName &&
                        p.EventKey == @event.EventKey)
            .ToListAsync(ct);

        foreach (var pointer in waitingPointers)
        {
            pointer.Status = (int)StepStatus.Pending;
            pointer.EventData = @event.EventDataJson;
            pointer.WorkflowInstance.Status = (int)WorkflowStatus.Runnable;
            pointer.WorkflowInstance.NextExecutionTime = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WorkflowEvent>> ConsumeEventsAsync(
        string eventName,
        string eventKey,
        CancellationToken ct = default)
    {
        var entities = await _db.WorkflowEvents
            .Where(e => e.EventName == eventName &&
                        e.EventKey == eventKey &&
                        !e.IsConsumed)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        foreach (var entity in entities)
        {
            entity.IsConsumed = true;
            entity.ConsumedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return entities.Select(e => new WorkflowEvent
        {
            Id = e.Id,
            EventName = e.EventName,
            EventKey = e.EventKey,
            EventDataJson = e.EventDataJson,
            CreatedAt = e.CreatedAt,
            IsConsumed = true,
            ConsumedAt = e.ConsumedAt
        }).ToList();
    }

    // ── Mapping helpers ──────────────────────────────────────────────────────

    private static WorkflowInstanceEntity ToEntity(WorkflowInstance domain)
    {
        var entity = new WorkflowInstanceEntity
        {
            Id = domain.Id,
            WorkflowName = domain.WorkflowName,
            Version = domain.Version,
            DataType = domain.DataType,
            DataJson = domain.DataJson,
            Status = (int)domain.Status,
            CreatedAt = domain.CreatedAt,
            CompleteAt = domain.CompleteAt,
            NextExecutionTime = domain.NextExecutionTime,
            CorrelationId = domain.CorrelationId,
            Error = domain.Error
        };

        entity.ExecutionPointers = domain.ExecutionPointers
            .Select(p => ToPointerEntity(p, domain.Id))
            .ToList();

        return entity;
    }

    private static void MapToEntity(WorkflowInstance domain, WorkflowInstanceEntity entity)
    {
        entity.WorkflowName = domain.WorkflowName;
        entity.Version = domain.Version;
        entity.DataType = domain.DataType;
        entity.DataJson = domain.DataJson;
        entity.Status = (int)domain.Status;
        entity.CompleteAt = domain.CompleteAt;
        entity.NextExecutionTime = domain.NextExecutionTime;
        entity.CorrelationId = domain.CorrelationId;
        entity.Error = domain.Error;

        // Sync execution pointers: update existing, add new.
        var existingById = entity.ExecutionPointers.ToDictionary(p => p.Id);

        foreach (var domainPointer in domain.ExecutionPointers)
        {
            if (existingById.TryGetValue(domainPointer.Id, out var existing))
            {
                MapPointerToEntity(domainPointer, existing);
            }
            else
            {
                entity.ExecutionPointers.Add(ToPointerEntity(domainPointer, domain.Id));
            }
        }

        // Remove deleted pointers.
        var domainIds = domain.ExecutionPointers.Select(p => p.Id).ToHashSet();
        entity.ExecutionPointers.RemoveAll(p => !domainIds.Contains(p.Id));
    }

    private static ExecutionPointerEntity ToPointerEntity(ExecutionPointer domain, Guid instanceId)
    {
        var entity = new ExecutionPointerEntity { Id = domain.Id, WorkflowInstanceId = instanceId };
        MapPointerToEntity(domain, entity);
        return entity;
    }

    private static void MapPointerToEntity(ExecutionPointer domain, ExecutionPointerEntity entity)
    {
        entity.StepIndex = domain.StepIndex;
        entity.StepName = domain.StepName;
        entity.Status = (int)domain.Status;
        entity.AttemptCount = domain.AttemptCount;
        entity.StartTime = domain.StartTime;
        entity.EndTime = domain.EndTime;
        entity.RetryAfter = domain.RetryAfter;
        entity.EventName = domain.EventName;
        entity.EventKey = domain.EventKey;
        entity.EventData = domain.EventData;
        entity.Error = domain.Error;
        entity.PersistenceDataJson = JsonSerializer.Serialize(domain.PersistenceData, _jsonOptions);
    }

    private static WorkflowInstance ToDomain(WorkflowInstanceEntity entity)
    {
        var domain = new WorkflowInstance
        {
            Id = entity.Id,
            WorkflowName = entity.WorkflowName,
            Version = entity.Version,
            DataType = entity.DataType,
            DataJson = entity.DataJson,
            Status = (WorkflowStatus)entity.Status,
            CreatedAt = entity.CreatedAt,
            CompleteAt = entity.CompleteAt,
            NextExecutionTime = entity.NextExecutionTime,
            CorrelationId = entity.CorrelationId,
            Error = entity.Error,
            RowVersion = entity.RowVersion
        };

        domain.ExecutionPointers = entity.ExecutionPointers
            .Select(ToPointerDomain)
            .ToList();

        return domain;
    }

    private static ExecutionPointer ToPointerDomain(ExecutionPointerEntity entity)
    {
        var persistenceData = string.IsNullOrEmpty(entity.PersistenceDataJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.PersistenceDataJson) ?? [];

        return new ExecutionPointer
        {
            Id = entity.Id,
            StepIndex = entity.StepIndex,
            StepName = entity.StepName,
            Status = (StepStatus)entity.Status,
            AttemptCount = entity.AttemptCount,
            StartTime = entity.StartTime,
            EndTime = entity.EndTime,
            RetryAfter = entity.RetryAfter,
            EventName = entity.EventName,
            EventKey = entity.EventKey,
            EventData = entity.EventData,
            Error = entity.Error,
            PersistenceData = persistenceData
        };
    }
}
