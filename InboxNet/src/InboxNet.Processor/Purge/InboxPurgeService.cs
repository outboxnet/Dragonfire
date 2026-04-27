using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InboxNet.Interfaces;
using InboxNet.Options;

namespace InboxNet.Processor.Purge;

/// <summary>
/// Periodically deletes processed/dead-lettered inbox messages and old handler-attempt rows.
/// Without this job the inbox tables grow unbounded, since the dispatcher only marks rows
/// as <c>Processed</c>/<c>DeadLettered</c> — it never deletes them. Retention windows are
/// configured via <see cref="InboxPurgeOptions"/>.
/// </summary>
public sealed class InboxPurgeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InboxPurgeOptions _options;
    private readonly ILogger<InboxPurgeService> _logger;

    public InboxPurgeService(
        IServiceScopeFactory scopeFactory,
        IOptions<InboxPurgeOptions> options,
        ILogger<InboxPurgeService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Inbox purge job started. Interval: {Interval}, retain processed: {RetainProcessed}, retain attempts: {RetainAttempts}",
            _options.PurgeInterval, _options.RetainProcessed, _options.RetainAttempts);

        // Stagger the first run so multiple replicas don't all hit the table at startup.
        var firstDelay = TimeSpan.FromSeconds(Random.Shared.Next(30, 90));
        try { await Task.Delay(firstDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inbox purge run failed");
            }

            try
            {
                await Task.Delay(_options.PurgeInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var store = sp.GetRequiredService<IInboxStore>();
        var attemptStore = sp.GetRequiredService<IInboxHandlerAttemptStore>();

        var now = DateTimeOffset.UtcNow;
        var messageCutoff = now - _options.RetainProcessed;
        var attemptCutoff = now - _options.RetainAttempts;

        // Delete attempts first — keeping the parent message ensures we never orphan an
        // attempt row that lacks its owning message in the rare overlap window.
        var attemptsDeleted = await attemptStore.PurgeOldAttemptsAsync(attemptCutoff, ct);
        var messagesDeleted = await store.PurgeProcessedMessagesAsync(messageCutoff, ct);

        if (attemptsDeleted > 0 || messagesDeleted > 0)
        {
            _logger.LogInformation(
                "Inbox purge: deleted {Messages} message(s) older than {MessageCutoff:o} and {Attempts} attempt(s) older than {AttemptCutoff:o}",
                messagesDeleted, messageCutoff, attemptsDeleted, attemptCutoff);
        }
        else
        {
            _logger.LogDebug("Inbox purge: nothing to delete this run");
        }
    }
}
