using System.Net.Http.Json;

namespace Dragonfire.TraceKit.SampleApp.Clients;

/// <summary>
/// Typed client for https://catfact.ninja — used to demonstrate a second third-party
/// HttpClient and parallel fan-out tracing in <see cref="Controllers.DemoApiController"/>.
/// </summary>
public sealed class CatFactsClient
{
    private readonly HttpClient _http;

    public CatFactsClient(HttpClient http) => _http = http;

    public Task<CatFact?> GetRandomFactAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<CatFact>("fact", ct);

    public sealed record CatFact(string Fact, int Length);
}
