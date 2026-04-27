using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dragonfire.Caching.Core;

/// <summary>
/// Background service that drains <see cref="ChannelInvalidationQueue"/> and removes
/// tagged cache keys from the provider. Configure concurrency via
/// <see cref="InvalidationQueueOptions.ConsumerCount"/>.
/// </summary>
internal sealed class InvalidationWorker : BackgroundService
{
    private readonly ChannelInvalidationQueue _queue;
    private readonly ITagIndex _tagIndex;
    private readonly ICacheProvider _cache;
    private readonly ITemplateResolver _resolver;
    private readonly CacheSettings _settings;
    private readonly ILogger<InvalidationWorker> _logger;

    public InvalidationWorker(
        ChannelInvalidationQueue queue,
        ITagIndex tagIndex,
        ICacheProvider cache,
        ITemplateResolver resolver,
        IOptions<CacheSettings> settings,
        ILogger<InvalidationWorker> logger)
    {
        _queue = queue;
        _tagIndex = tagIndex;
        _cache = cache;
        _resolver = resolver;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InvalidationWorker started.");

        await foreach (var request in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(request, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invalidation for operation '{Operation}'.", request.OperationName);
            }
        }

        _logger.LogInformation("InvalidationWorker stopped.");
    }

    private async Task ProcessAsync(InvalidationRequest request, CancellationToken ct)
    {
        if (!_settings.Operations.TryGetValue(request.OperationName, out var policy))
        {
            _logger.LogDebug("No policy found for operation '{Operation}', skipping.", request.OperationName);
            return;
        }

        foreach (var tagTemplate in policy.InvalidatesTags)
        {
            var tag = _resolver.Resolve(tagTemplate, request.Parameters);
            var keys = await _tagIndex.GetKeysAsync(tag, ct);

            foreach (var key in keys)
                await _cache.RemoveAsync(key, ct);

            await _tagIndex.RemoveTagAsync(tag, ct);

            _logger.LogDebug("Invalidated {Count} entries under tag '{Tag}'.", keys.Count, tag);
        }
    }
}
