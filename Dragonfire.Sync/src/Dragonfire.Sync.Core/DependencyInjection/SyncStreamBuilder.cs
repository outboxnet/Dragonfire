using Microsoft.Extensions.DependencyInjection;
using Dragonfire.Sync.Abstractions;
using Dragonfire.Sync.Core.Configuration;

namespace Dragonfire.Sync.Core.DependencyInjection;

internal sealed class SyncStreamBuilder<TClient, TDto> : ISyncStreamBuilder<TClient, TDto>
    where TClient : class
    where TDto : class
{
    private readonly IServiceCollection _services;
    private readonly SyncStateKey _key;
    private readonly StreamConfiguration _configuration = new();
    private SyncPipeline<TClient, TDto>.FetchDelegate? _fetch;
    private SyncPipeline<TClient, TDto>? _customPipeline;
    private bool _hasHandler;

    public SyncStreamBuilder(IServiceCollection services, SyncStateKey key)
    {
        _services = services;
        _key = key;
    }

    public ISyncStreamBuilder<TClient, TDto> WithFetch(SyncPipeline<TClient, TDto>.FetchDelegate fetch)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        if (_customPipeline is not null)
        {
            throw new InvalidOperationException("WithFetch cannot be combined with WithPipeline.");
        }
        _fetch = fetch;
        return this;
    }

    public ISyncStreamBuilder<TClient, TDto> WithPipeline(SyncPipeline<TClient, TDto> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (_fetch is not null)
        {
            throw new InvalidOperationException("WithPipeline cannot be combined with WithFetch.");
        }
        if (pipeline.Key != _key)
        {
            throw new InvalidOperationException(
                $"Pipeline Key '{pipeline.Key}' does not match registered stream key '{_key}'.");
        }
        _customPipeline = pipeline;
        return this;
    }

    public ISyncStreamBuilder<TClient, TDto> HandledBy<THandler>()
        where THandler : class, ISyncDataHandler<TDto>
    {
        _services.AddScoped<ISyncDataHandler<TDto>, THandler>();
        _hasHandler = true;
        return this;
    }

    public ISyncStreamBuilder<TClient, TDto> HandledBy(ISyncDataHandler<TDto> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _services.AddSingleton(handler);
        _hasHandler = true;
        return this;
    }

    public ISyncStreamBuilder<TClient, TDto> Configure(Action<StreamConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_configuration);
        return this;
    }

    public ISyncStreamBuilder<TClient, TDto> WithSchedule(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "SyncInterval must be positive.");
        }
        _configuration.SyncInterval = interval;
        return this;
    }

    public IServiceCollection Build()
    {
        if (_customPipeline is null && _fetch is null)
        {
            throw new InvalidOperationException("Either WithFetch(...) or WithPipeline(...) must be called.");
        }
        if (!_hasHandler)
        {
            throw new InvalidOperationException("HandledBy<THandler>() (or HandledBy(instance)) must be called.");
        }

        var registration = _customPipeline is { } pipeline
            ? SyncStreamRegistration.Create(pipeline, _configuration)
            : SyncStreamRegistration.Create<TClient, TDto>(_key, _fetch!, _configuration);

        _services.AddSingleton(registration);
        return _services;
    }
}
