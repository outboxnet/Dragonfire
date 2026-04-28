using System.CommandLine;
using Dragonfire.ApiClientGen;

var inputOption = new Option<FileInfo>(
    aliases: new[] { "--input", "-i" },
    description: "Path to the Postman v2.1 collection JSON file.")
    { IsRequired = true };

var outputOption = new Option<DirectoryInfo>(
    aliases: new[] { "--output", "-o" },
    description: "Directory where the generated client project will be written.")
    { IsRequired = true };

var namespaceOption = new Option<string>(
    aliases: new[] { "--namespace", "-n" },
    description: "Root namespace for the generated client (e.g. Acme.BillingClient).")
    { IsRequired = true };

var clientNameOption = new Option<string>(
    aliases: new[] { "--client-name", "-c" },
    description: "Class-name prefix (e.g. 'Billing' produces BillingClient, IBillingClient).")
    { IsRequired = true };

var targetFrameworkOption = new Option<string>(
    aliases: new[] { "--target-framework", "-t" },
    getDefaultValue: () => "net8.0",
    description: "Target framework moniker for the generated csproj.");

var responseExamplesOption = new Option<FileInfo?>(
    aliases: new[] { "--response-examples" },
    description: "Optional JSON file mapping operation names to override response example bodies.");

var baseUrlOption = new Option<string?>(
    aliases: new[] { "--base-url" },
    description: "Override the base URL detected from the collection.");

var cleanOption = new Option<bool>(
    aliases: new[] { "--clean" },
    description: "Delete the output directory before writing.");

var dryRunOption = new Option<bool>(
    aliases: new[] { "--dry-run" },
    description: "Print the plan without writing any files.");

var floatsAsDoubleOption = new Option<bool>(
    aliases: new[] { "--floats-as-double" },
    description: "Use 'double' for non-integer JSON numbers (default: 'decimal').");

var root = new RootCommand("Generate a typed C# HTTP client from a Postman v2.1 collection.")
{
    inputOption,
    outputOption,
    namespaceOption,
    clientNameOption,
    targetFrameworkOption,
    responseExamplesOption,
    baseUrlOption,
    cleanOption,
    dryRunOption,
    floatsAsDoubleOption,
};

root.SetHandler(context =>
{
    var input            = context.ParseResult.GetValueForOption(inputOption)!;
    var output           = context.ParseResult.GetValueForOption(outputOption)!;
    var ns               = context.ParseResult.GetValueForOption(namespaceOption)!;
    var clientName       = context.ParseResult.GetValueForOption(clientNameOption)!;
    var targetFramework  = context.ParseResult.GetValueForOption(targetFrameworkOption)!;
    var responseExamples = context.ParseResult.GetValueForOption(responseExamplesOption);
    var baseUrl          = context.ParseResult.GetValueForOption(baseUrlOption);
    var clean            = context.ParseResult.GetValueForOption(cleanOption);
    var dryRun           = context.ParseResult.GetValueForOption(dryRunOption);
    var floatsAsDouble   = context.ParseResult.GetValueForOption(floatsAsDoubleOption);

    var options = new GeneratorOptions
    {
        InputPath = input.FullName,
        OutputPath = output.FullName,
        Namespace = ns,
        ClientName = clientName,
        TargetFramework = targetFramework,
        ResponseExamplesPath = responseExamples?.FullName,
        BaseUrlOverride = baseUrl,
        Clean = clean,
        DryRun = dryRun,
        FloatsAsDouble = floatsAsDouble,
    };

    var pipeline = new GeneratorPipeline();
    var result = pipeline.Generate(options);

    Console.WriteLine($"Operations:    {result.Ir.Operations.Count}");
    Console.WriteLine($"Models:        {result.Ir.Types.Count(t => t.IsClass)}");
    Console.WriteLine($"Files:         {result.Files.Count}");
    Console.WriteLine($"Output:        {options.OutputPath}");

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (var w in result.Warnings) Console.WriteLine("  - " + w);
    }

    if (options.DryRun)
    {
        Console.WriteLine();
        Console.WriteLine("(dry-run) files that would be written:");
        foreach (var f in result.Files) Console.WriteLine("  " + f.RelativePath);
        return;
    }

    pipeline.WriteToDisk(result, options);
    Console.WriteLine();
    Console.WriteLine("Done.");
});

return await root.InvokeAsync(args);
