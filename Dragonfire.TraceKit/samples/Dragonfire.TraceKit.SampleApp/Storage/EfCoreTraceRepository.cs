using System.Text.Json;
using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.TraceKit.SampleApp.Storage;

/// <summary>
/// Persists captured <see cref="ApiTrace"/> rows via EF Core. The write side runs inside
/// the per-trace DI scope opened by TraceKit's background drain, so the <see cref="TraceDbContext"/>
/// is short-lived and safe. The read side opens its own scope per query (the MVC viewer
/// is request-scoped and resolves a fresh context).
/// </summary>
public sealed class EfCoreTraceRepository : ITraceRepository, ITraceQuery
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly TraceDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;

    public EfCoreTraceRepository(TraceDbContext db, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task SaveAsync(ApiTrace trace, CancellationToken cancellationToken)
    {
        _db.Traces.Add(Map(trace));
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<TraceSession> ListSessions(int take = 100)
    {
        // ListSessions / GetSession are called from MVC actions that do not use this
        // repository's scoped DbContext — open a dedicated scope so we never reuse a
        // context across threads.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TraceDbContext>();

        var inboundKind = (byte)TraceKind.Inbound;
        var outboundKind = (byte)TraceKind.OutboundThirdParty;

        // One session per correlation id. Pull the inbound row for headline data and a
        // count of outbound rows in a single round-trip.
        var sessions = db.Traces
            .AsNoTracking()
            .GroupBy(t => t.CorrelationId)
            .Select(g => new
            {
                CorrelationId = g.Key,
                Inbound = g.Where(t => t.Kind == inboundKind).OrderBy(t => t.Sequence).FirstOrDefault(),
                OutboundCount = g.Count(t => t.Kind == outboundKind),
                Earliest = g.Min(t => t.StartedAtUtc),
            })
            .OrderByDescending(x => x.Earliest)
            .Take(take)
            .ToList();

        return sessions
            .Select(s => new TraceSession(
                CorrelationId: s.CorrelationId,
                StartedAtUtc: s.Inbound?.StartedAtUtc ?? s.Earliest,
                Method: s.Inbound?.Method ?? string.Empty,
                Url: s.Inbound?.Url ?? string.Empty,
                StatusCode: s.Inbound?.StatusCode,
                OutboundCallCount: s.OutboundCount,
                Duration: s.Inbound is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(s.Inbound.DurationMs),
                TenantId: s.Inbound?.TenantId))
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ApiTrace> GetSession(string correlationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TraceDbContext>();

        return db.Traces
            .AsNoTracking()
            .Where(t => t.CorrelationId == correlationId)
            .OrderBy(t => t.Sequence)
            .AsEnumerable()
            .Select(Map)
            .ToList();
    }

    private static TraceEntity Map(ApiTrace trace) => new()
    {
        TraceId = trace.TraceId,
        CorrelationId = trace.CorrelationId,
        Sequence = trace.Sequence,
        Kind = (byte)trace.Kind,
        Method = trace.Method,
        Url = trace.Url,
        OperationName = trace.OperationName,
        StatusCode = trace.StatusCode,
        StartedAtUtc = trace.StartedAtUtc,
        CompletedAtUtc = trace.CompletedAtUtc,
        DurationMs = (int)trace.Duration.TotalMilliseconds,
        RequestContentType = trace.RequestContentType,
        ResponseContentType = trace.ResponseContentType,
        RequestHeadersJson = JsonSerializer.Serialize(trace.RequestHeaders, JsonOptions),
        ResponseHeadersJson = JsonSerializer.Serialize(trace.ResponseHeaders, JsonOptions),
        RequestBody = trace.RequestBody,
        ResponseBody = trace.ResponseBody,
        ExceptionType = trace.ExceptionType,
        ExceptionMessage = trace.ExceptionMessage,
        TenantId = trace.TenantId,
        UserId = trace.UserId,
        TagsJson = trace.Tags.Count == 0 ? null : JsonSerializer.Serialize(trace.Tags, JsonOptions),
    };

    private static ApiTrace Map(TraceEntity row)
    {
        var trace = new ApiTrace
        {
            TraceId = row.TraceId,
            CorrelationId = row.CorrelationId,
            Sequence = row.Sequence,
            Kind = (TraceKind)row.Kind,
            Method = row.Method,
            Url = row.Url,
            OperationName = row.OperationName,
            StatusCode = row.StatusCode,
            StartedAtUtc = row.StartedAtUtc,
            CompletedAtUtc = row.CompletedAtUtc,
            RequestContentType = row.RequestContentType,
            ResponseContentType = row.ResponseContentType,
            RequestBody = row.RequestBody,
            ResponseBody = row.ResponseBody,
            ExceptionType = row.ExceptionType,
            ExceptionMessage = row.ExceptionMessage,
            TenantId = row.TenantId,
            UserId = row.UserId,
        };

        var requestHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RequestHeadersJson);
        if (requestHeaders is not null)
            foreach (var kv in requestHeaders) trace.RequestHeaders[kv.Key] = kv.Value;

        var responseHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(row.ResponseHeadersJson);
        if (responseHeaders is not null)
            foreach (var kv in responseHeaders) trace.ResponseHeaders[kv.Key] = kv.Value;

        if (!string.IsNullOrEmpty(row.TagsJson))
        {
            var tags = JsonSerializer.Deserialize<Dictionary<string, string?>>(row.TagsJson);
            if (tags is not null)
                foreach (var kv in tags) trace.Tags[kv.Key] = kv.Value;
        }

        return trace;
    }
}
