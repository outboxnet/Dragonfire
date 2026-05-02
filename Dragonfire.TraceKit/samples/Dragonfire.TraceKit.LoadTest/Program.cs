using System.Diagnostics;

// ----------------------------------------------------------------------------
// Dragonfire.TraceKit — minimal load runner
//
// Usage:
//   dotnet run -- [--url http://localhost:5080] [--total 1000] [--concurrency 32]
//
// Fires a fixed number of requests across a small pool of workers against the
// SampleApp's demo endpoints, then queries /traces to report how many sessions
// persisted. The point is to put the middleware, the bounded channel, the
// background drain, and EF Core under sustained concurrent pressure — NOT to
// stress the third-party APIs.
// ----------------------------------------------------------------------------

var url         = ArgValue("--url", args)         ?? "http://localhost:5080";
var totalArg    = ArgValue("--total", args)       ?? "1000";
var parallelArg = ArgValue("--concurrency", args) ?? "32";

if (!int.TryParse(totalArg, out var total) || total <= 0)
{
    Console.Error.WriteLine($"Invalid --total: {totalArg}");
    return 1;
}
if (!int.TryParse(parallelArg, out var concurrency) || concurrency <= 0)
{
    Console.Error.WriteLine($"Invalid --concurrency: {parallelArg}");
    return 1;
}

var baseUri = new Uri(url, UriKind.Absolute);
using var http = new HttpClient
{
    BaseAddress = baseUri,
    Timeout = TimeSpan.FromSeconds(60),
};

// One short probe so we fail fast if the SampleApp isn't running.
try
{
    using var probe = await http.GetAsync("/", HttpCompletionOption.ResponseHeadersRead);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Cannot reach {baseUri}: {ex.Message}");
    Console.Error.WriteLine("Start the sample first:");
    Console.Error.WriteLine("  cd samples/Dragonfire.TraceKit.SampleApp && dotnet run");
    return 2;
}

Console.WriteLine($"Target           : {baseUri}");
Console.WriteLine($"Total requests   : {total}");
Console.WriteLine($"Concurrency      : {concurrency}");
Console.WriteLine();

var endpoints = new (string Method, string Path, string? Body)[]
{
    ("GET",  "/api/demo/posts/1",        null),
    ("GET",  "/api/demo/posts/1/full",   null),
    ("GET",  "/api/demo/dashboard/2",    null),
    ("POST", "/api/demo/echo",
        """{"title":"hi","body":"world","password":"hunter2","apiKey":"k-9999"}"""),
};

var latencies = new List<long>(total);
var latencyLock = new object();
var statusCounts = new Dictionary<int, int>();
var errors = 0;

var queue = new Queue<int>(Enumerable.Range(0, total));
var queueLock = new object();
var rngSeed = Environment.TickCount;

var sw = Stopwatch.StartNew();

await Parallel.ForEachAsync(
    Enumerable.Range(0, concurrency),
    new ParallelOptions { MaxDegreeOfParallelism = concurrency },
    async (worker, ct) =>
    {
        var rng = new Random(unchecked(rngSeed + worker * 7919));
        while (true)
        {
            lock (queueLock)
            {
                if (queue.Count == 0) return;
                queue.Dequeue();
            }

            var ep = endpoints[rng.Next(endpoints.Length)];
            var requestSw = Stopwatch.StartNew();
            int? status = null;
            try
            {
                using var req = new HttpRequestMessage(new HttpMethod(ep.Method), ep.Path);
                if (ep.Body is not null)
                    req.Content = new StringContent(ep.Body, System.Text.Encoding.UTF8, "application/json");

                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                status = (int)resp.StatusCode;
                // Drain so the server-side trace records a real response.
                await resp.Content.ReadAsByteArrayAsync(ct);
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
            finally
            {
                requestSw.Stop();
                lock (latencyLock)
                {
                    latencies.Add(requestSw.ElapsedMilliseconds);
                    if (status is not null)
                    {
                        statusCounts.TryGetValue(status.Value, out var c);
                        statusCounts[status.Value] = c + 1;
                    }
                }
            }
        }
    });

sw.Stop();

PrintReport(latencies, statusCounts, errors, sw.Elapsed, total);

Console.WriteLine();
Console.WriteLine("Letting the trace drain catch up...");
await Task.Delay(TimeSpan.FromSeconds(2));

await ReportPersistenceAsync(http);

return 0;

// ----------------------------------------------------------------------------
static string? ArgValue(string name, string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

static void PrintReport(
    List<long> latencies, Dictionary<int, int> statusCounts, int errors,
    TimeSpan wallClock, int total)
{
    latencies.Sort();
    var ok = latencies.Count;
    var rps = wallClock.TotalSeconds > 0 ? ok / wallClock.TotalSeconds : 0;

    Console.WriteLine("==== Results =====================================================");
    Console.WriteLine($"Wall clock        : {wallClock.TotalSeconds:F2} s");
    Console.WriteLine($"Completed         : {ok} / {total}");
    Console.WriteLine($"Errors            : {errors}");
    Console.WriteLine($"Throughput        : {rps:F1} req/s");
    if (latencies.Count > 0)
    {
        Console.WriteLine($"Latency  min/avg  : {latencies[0]} ms / {latencies.Average():F1} ms");
        Console.WriteLine($"Latency  p50      : {Pct(latencies, 0.50)} ms");
        Console.WriteLine($"Latency  p90      : {Pct(latencies, 0.90)} ms");
        Console.WriteLine($"Latency  p99      : {Pct(latencies, 0.99)} ms");
        Console.WriteLine($"Latency  max      : {latencies[^1]} ms");
    }
    Console.WriteLine("Status codes      :");
    foreach (var kv in statusCounts.OrderBy(k => k.Key))
        Console.WriteLine($"  {kv.Key,-4}            : {kv.Value}");
}

static long Pct(List<long> sorted, double p)
{
    if (sorted.Count == 0) return 0;
    var idx = (int)Math.Ceiling(p * sorted.Count) - 1;
    if (idx < 0) idx = 0;
    if (idx >= sorted.Count) idx = sorted.Count - 1;
    return sorted[idx];
}

static async Task ReportPersistenceAsync(HttpClient http)
{
    try
    {
        // The /traces page is HTML — sniff a count from the visible "Sessions (N)" text.
        using var resp = await http.GetAsync("/traces");
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"Could not reach /traces: HTTP {(int)resp.StatusCode}");
            return;
        }
        var html = await resp.Content.ReadAsStringAsync();
        var marker = "Sessions <span class=\"text-muted fs-6\">(";
        var i = html.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0)
        {
            Console.WriteLine("Could not parse session count from /traces.");
            return;
        }
        var start = i + marker.Length;
        var end = html.IndexOf(')', start);
        if (end < 0) return;
        Console.WriteLine($"Sessions persisted: {html[start..end]}  (visit {http.BaseAddress}traces)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Persistence check failed: {ex.Message}");
    }
}