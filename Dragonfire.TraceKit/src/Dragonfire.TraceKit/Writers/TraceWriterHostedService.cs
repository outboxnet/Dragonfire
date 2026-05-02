using Dragonfire.TraceKit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dragonfire.TraceKit.Writers;

/// <summary>
/// Drains the in-memory channel and persists traces via <see cref="ITraceRepository"/>.
/// Repository failures are logged and swallowed so a misbehaving sink can never bring
/// down the API. The repository is resolved per-trace from a fresh DI scope so scoped
/// dependencies (DbContext, etc.) work correctly.
/// </summary>
internal sealed class TraceWriterHostedService : BackgroundService
{
    private readonly ChannelTraceWriter _writer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TraceWriterHostedService> _logger;

    public TraceWriterHostedService(
        ITraceWriter writer,
        IServiceScopeFactory scopeFactory,
        ILogger<TraceWriterHostedService> logger)
    {
        // ChannelTraceWriter is the concrete type registered as ITraceWriter; cast safely.
        _writer = (ChannelTraceWriter)writer;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _writer.Reader;

        try
        {
            await foreach (var trace in reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var repository = scope.ServiceProvider.GetRequiredService<ITraceRepository>();
                    await repository.SaveAsync(trace, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "TraceKit: trace repository threw while persisting CorrelationId={CorrelationId} Sequence={Sequence}; trace dropped.",
                        trace.CorrelationId, trace.Sequence);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
    }
}
