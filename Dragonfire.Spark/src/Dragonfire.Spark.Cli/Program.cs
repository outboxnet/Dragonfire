using System.CommandLine;
using Dragonfire.Spark;

var inputOption = new Option<FileInfo>(
    aliases: ["--input", "-i"],
    description: "Path to the source file: a HAR archive (.har) or a Charles Proxy JSON export (.json).")
    { IsRequired = true };

var outputOption = new Option<FileInfo>(
    aliases: ["--output", "-o"],
    description: "Path to write the generated Postman v2.1 collection JSON.")
    { IsRequired = true };

var nameOption = new Option<string?>(
    aliases: ["--name", "-n"],
    description: "Collection name embedded in the output (defaults to the input file name).");

var formatOption = new Option<string?>(
    aliases: ["--format", "-f"],
    description: "Force input format: 'har' or 'charles'. Auto-detected when omitted.");

var root = new RootCommand(
    "dragonfire-spark — convert a HAR archive or Charles Proxy JSON export to a Postman v2.1 collection.")
{
    inputOption,
    outputOption,
    nameOption,
    formatOption,
};

root.SetHandler(context =>
{
    var input  = context.ParseResult.GetValueForOption(inputOption)!;
    var output = context.ParseResult.GetValueForOption(outputOption)!;
    var name   = context.ParseResult.GetValueForOption(nameOption);
    var fmt    = context.ParseResult.GetValueForOption(formatOption);

    InputFormat? formatOverride = fmt?.ToLowerInvariant() switch
    {
        "har"     => InputFormat.Har,
        "charles" => InputFormat.Charles,
        null      => null,
        _         => throw new ArgumentException($"Unknown format '{fmt}'. Use 'har' or 'charles'."),
    };

    var options = new SparkOptions
    {
        InputPath      = input.FullName,
        OutputPath     = output.FullName,
        CollectionName = name,
        FormatOverride = formatOverride,
    };

    try
    {
        var pipeline = new SparkPipeline();
        var result   = pipeline.Run(options);

        Console.WriteLine($"Format:     {result.Format}");
        Console.WriteLine($"Requests:   {result.ItemCount}");
        Console.WriteLine($"Output:     {result.OutputPath}");
        Console.WriteLine();
        Console.WriteLine("Done.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        context.ExitCode = 1;
    }
});

return await root.InvokeAsync(args);
