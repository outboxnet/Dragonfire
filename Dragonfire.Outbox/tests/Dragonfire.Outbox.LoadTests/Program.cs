using Dragonfire.Outbox.LoadTests;

// ─────────────────────────────────────────────────────────────────────────────
// Dragonfire.Outbox Load Test
//
// Measures throughput and correctness of the full outbox pipeline against a
// real SQL Server database with a real embedded HTTP webhook receiver.
//
// Usage:
//   dotnet run                                  # defaults: 5 000 msgs, 20 threads
//   dotnet run -- --messages 10000              # larger run
//   dotnet run -- --concurrency 50              # more publisher threads
//   dotnet run -- --failure-rate 0.2            # 20% webhook failures → tests retries
//   dotnet run -- --connection "Server=..."     # custom SQL Server
//   dotnet run -- --keep-data                   # skip truncating tables before run
//   dotnet run -- --no-hmac                     # skip HMAC verification in receiver
//   dotnet run -- --help                        # print all options
// ─────────────────────────────────────────────────────────────────────────────

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return 0;
}

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║              Dragonfire.Outbox Load Test & Correctness Check         ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

var config = LoadTestConfig.Parse(args);

Console.WriteLine("Configuration:");
config.Print();
Console.WriteLine();

// Ctrl+C graceful shutdown
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    Console.WriteLine("\nCancelling...");
    e.Cancel = true;
    cts.Cancel();
};

int exitCode;
try
{
    var runner = new LoadTestRunner(config);
    var result = await runner.RunAsync(cts.Token);
    result.Print();
    exitCode = result.IsCorrect ? 0 : 1;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Load test cancelled.");
    exitCode = 2;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"Fatal error: {ex.Message}");
    Console.Error.WriteLine(ex.ToString());
    Console.ResetColor();
    exitCode = 3;
}

return exitCode;

// ─────────────────────────────────────────────────────────────────────────────

static void PrintHelp()
{
    Console.WriteLine("Dragonfire.Outbox Load Test");
    Console.WriteLine();
    Console.WriteLine("OPTIONS:");
    Console.WriteLine("  --messages <n>         Total messages to publish  (default: 5000)");
    Console.WriteLine("  --concurrency <n>      Concurrent publisher threads (default: 20)");
    Console.WriteLine("  --batch-size <n>       OutboxOptions.BatchSize     (default: 50)");
    Console.WriteLine("  --max-deliveries <n>   MaxConcurrentDeliveries     (default: 10)");
    Console.WriteLine("  --poll-ms <ms>         Cold-path polling interval  (default: 500)");
    Console.WriteLine("  --port <n>             Webhook receiver port       (default: 5556)");
    Console.WriteLine("  --secret <s>           HMAC signing secret         (default: built-in)");
    Console.WriteLine("  --failure-rate <0-1>   Fraction of 503 responses   (default: 0.0)");
    Console.WriteLine("  --drain-timeout <sec>  Wait for delivery to finish (default: 120)");
    Console.WriteLine("  --connection <cs>      SQL Server connection string (default: LocalDB)");
    Console.WriteLine("  --keep-data            Skip truncating tables before run");
    Console.WriteLine("  --no-hmac              Disable HMAC verification in receiver");
    Console.WriteLine("  --help                 Print this message");
    Console.WriteLine();
    Console.WriteLine("EXAMPLES:");
    Console.WriteLine("  dotnet run                               # quick baseline");
    Console.WriteLine("  dotnet run -- --messages 20000 --concurrency 50");
    Console.WriteLine("  dotnet run -- --failure-rate 0.15        # retry correctness");
    Console.WriteLine("  dotnet run -- --connection \"Server=myserver;Database=...\"");
}
