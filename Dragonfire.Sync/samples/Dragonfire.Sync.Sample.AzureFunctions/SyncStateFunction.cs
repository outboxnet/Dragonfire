using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Dragonfire.Sync.Abstractions;
using Dragonfire.Sync.Core;

namespace Dragonfire.Sync.Sample.AzureFunctions;

/// <summary>
/// HTTP endpoints for inspecting the most recent sync state and triggering an
/// ad-hoc run. Useful for ops dashboards or manual debugging.
/// </summary>
public sealed class SyncStateFunction
{
    private readonly ISyncStateReader _reader;
    private readonly ISyncRunner _runner;

    public SyncStateFunction(ISyncStateReader reader, ISyncRunner runner)
    {
        _reader = reader;
        _runner = runner;
    }

    [Function("GetAllSyncState")]
    public async Task<IActionResult> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sync/state")] HttpRequest req,
        CancellationToken cancellationToken)
        => new OkObjectResult(await _reader.GetAllAsync(cancellationToken));

    [Function("GetProviderSyncState")]
    public async Task<IActionResult> GetForProvider(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sync/state/{providerName}")] HttpRequest req,
        string providerName,
        CancellationToken cancellationToken)
        => new OkObjectResult(await _reader.GetByProviderAsync(providerName, cancellationToken));

    [Function("GetStreamSyncState")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "sync/state/{providerName}/{streamName}")] HttpRequest req,
        string providerName,
        string streamName,
        CancellationToken cancellationToken)
    {
        var state = await _reader.GetAsync(new SyncStateKey(providerName, streamName), cancellationToken);
        return state is null ? new NotFoundResult() : new OkObjectResult(state);
    }

    [Function("RunStreamNow")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sync/{providerName}/{streamName}/run")] HttpRequest req,
        string providerName,
        string streamName,
        CancellationToken cancellationToken)
    {
        try
        {
            await _runner.RunAsync(new SyncStateKey(providerName, streamName), cancellationToken);
            return new AcceptedResult();
        }
        catch (ArgumentException ex)
        {
            return new NotFoundObjectResult(new { error = ex.Message });
        }
    }
}
