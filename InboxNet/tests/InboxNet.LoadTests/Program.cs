using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using InboxNet.LoadTests;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var config = new LoadTestConfig();
configuration.GetSection("LoadTest").Bind(config);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("Cancellation requested — draining...");
    cts.Cancel();
};

Console.WriteLine("Running InboxNet load test:");
Console.WriteLine($"  messages={config.TotalMessages} publisher_concurrency={config.PublisherConcurrency}");
Console.WriteLine($"  batch_size={config.BatchSize} max_dispatch={config.MaxConcurrentDispatch}");
Console.WriteLine($"  cold_poll={config.ColdPollingIntervalMs}ms port={config.ReceiverPort}");
Console.WriteLine($"  failure_rate={config.HandlerFailureRate.ToString("F2", CultureInfo.InvariantCulture)}");
Console.WriteLine();

try
{
    var runner = new LoadTestRunner(config);
    var result = await runner.RunAsync(cts.Token);
    Console.WriteLine(result.Format());
    return result.IsCorrect ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Load test cancelled.");
    return 130;
}
catch (HostAbortedException)
{
    // Thrown by dotnet-ef's HostFactoryResolver after it has captured the IServiceProvider
    // from WebApplication.Build(). Expected — let it propagate so the tooling can detect it.
    throw;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Load test failed: {ex}");
    return 1;
}
