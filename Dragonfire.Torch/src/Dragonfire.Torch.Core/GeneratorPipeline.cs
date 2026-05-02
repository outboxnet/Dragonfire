using System.Text.Json;
using Dragonfire.Torch.Emit;
using Dragonfire.Torch.Har;
using Dragonfire.Torch.Postman;
using Dragonfire.Torch.Schema;

namespace Dragonfire.Torch;

public sealed class GeneratorOptions
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public required string Namespace { get; init; }
    public required string ClientName { get; init; }
    public string TargetFramework { get; init; } = "net8.0";
    public string? ResponseExamplesPath { get; init; }
    public string? BaseUrlOverride { get; init; }
    public bool Clean { get; init; }
    public bool DryRun { get; init; }
    public bool FloatsAsDouble { get; init; }
}

public sealed class GeneratorResult
{
    public required IReadOnlyList<GeneratedFile> Files { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required ClientIR Ir { get; init; }
}

/// <summary>
/// Orchestrates the parse -> infer -> shape -> render pipeline. Pure with
/// respect to the file system: <see cref="Generate"/> returns the file list;
/// <see cref="WriteToDisk"/> is the only place that touches disk.
/// </summary>
public sealed class GeneratorPipeline
{
    public GeneratorResult Generate(GeneratorOptions options)
    {
        var parseOptions = new ParseOptions
        {
            Namespace = options.Namespace,
            ClientName = options.ClientName,
            BaseUrlOverride = options.BaseUrlOverride,
            ResponseExamples = LoadResponseExamples(options.ResponseExamplesPath),
            FloatsAsDouble = options.FloatsAsDouble,
        };

        var ir = IsHar(options.InputPath)
            ? ParseHar(options.InputPath, parseOptions)
            : ParsePostman(options.InputPath, parseOptions);

        var files = Render(ir, options);

        return new GeneratorResult
        {
            Files = files,
            Warnings = ir.Warnings,
            Ir = ir,
        };
    }

    private static bool IsHar(string path) =>
        string.Equals(Path.GetExtension(path), ".har", StringComparison.OrdinalIgnoreCase);

    private static ClientIR ParsePostman(string path, ParseOptions opts)
    {
        var json = File.ReadAllText(path);
        var collection = JsonSerializer.Deserialize<PostmanCollection>(json, JsonOpts())
            ?? throw new InvalidOperationException($"Failed to parse Postman collection at '{path}'.");
        return new PostmanParser(opts).Parse(collection);
    }

    private static ClientIR ParseHar(string path, ParseOptions opts)
    {
        var json = File.ReadAllText(path);
        var har = JsonSerializer.Deserialize<HarFile>(json, JsonOpts())
            ?? throw new InvalidOperationException($"Failed to parse HAR file at '{path}'.");
        return new HarParser(opts).Parse(har.Log);
    }

    public void WriteToDisk(GeneratorResult result, GeneratorOptions options)
    {
        if (options.DryRun) return;

        if (options.Clean && Directory.Exists(options.OutputPath))
            Directory.Delete(options.OutputPath, recursive: true);

        Directory.CreateDirectory(options.OutputPath);

        foreach (var file in result.Files)
        {
            var fullPath = Path.Combine(options.OutputPath, file.RelativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, file.Contents);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement>? LoadResponseExamples(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.Clone();
        }
        return dict;
    }

    private static List<GeneratedFile> Render(ClientIR ir, GeneratorOptions options)
    {
        var renderer = new TemplateRenderer();
        var files = new List<GeneratedFile>();

        files.Add(new GeneratedFile
        {
            RelativePath = $"{ir.Namespace}.csproj",
            Contents = renderer.Render("Csproj", ModelShaper.Csproj(ir, options.TargetFramework)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = "Constants.cs",
            Contents = renderer.Render("Constants", ModelShaper.Constants(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = "Endpoints.cs",
            Contents = renderer.Render("Endpoints", ModelShaper.Endpoints(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = $"{ir.ClientName}ClientOptions.cs",
            Contents = renderer.Render("ClientOptions", ModelShaper.ClientOptions(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = $"{ir.ClientName}LoggingOptions.cs",
            Contents = renderer.Render("LoggingOptions", ModelShaper.Prefixed(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = "ApiResponse.cs",
            Contents = renderer.Render("ApiResponse", ModelShaper.Prefixed(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = $"I{ir.ClientName}RequestSigner.cs",
            Contents = renderer.Render("Signer", ModelShaper.Prefixed(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = $"I{ir.ClientName}HttpLogger.cs",
            Contents = renderer.Render("Logger", ModelShaper.Prefixed(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = $"I{ir.ClientName}ErrorHandler.cs",
            Contents = renderer.Render("ErrorHandler", ModelShaper.Prefixed(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = $"I{ir.ClientName}Client.cs",
            Contents = renderer.Render("ClientInterface", ModelShaper.ClientInterface(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = $"{ir.ClientName}Client.cs",
            Contents = renderer.Render("Client", ModelShaper.ClientImpl(ir)),
        });
        files.Add(new GeneratedFile
        {
            RelativePath = $"{ir.ClientName}ServiceCollectionExtensions.cs",
            Contents = renderer.Render("DiExtensions", ModelShaper.Prefixed(ir)),
        });

        // Models — one file per non-empty class type. Stub classes still emit
        // (the user fills them in post-generation).
        foreach (var type in ir.Types.Where(t => t.IsClass))
        {
            files.Add(new GeneratedFile
            {
                RelativePath = Path.Combine("Models", type.Name + ".cs"),
                Contents = renderer.Render("Model", ModelShaper.ModelFile(ir, type)),
            });
        }

        return files;
    }

    private static JsonSerializerOptions JsonOpts() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
