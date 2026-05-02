using System.Text.Encodings.Web;
using System.Text.Json;
using Dragonfire.Spark.Charles;
using Dragonfire.Spark.Converters;
using Dragonfire.Spark.Har;
using Dragonfire.Spark.Postman;

namespace Dragonfire.Spark;

public sealed class SparkOptions
{
    public required string InputPath  { get; init; }
    public required string OutputPath { get; init; }

    /// <summary>
    /// Name embedded in the generated collection's info block.
    /// Defaults to the input file name without extension.
    /// </summary>
    public string? CollectionName { get; init; }

    /// <summary>
    /// Override the auto-detected source format.
    /// <c>null</c> = auto-detect from file extension and JSON structure.
    /// </summary>
    public InputFormat? FormatOverride { get; init; }
}

public enum InputFormat
{
    Har,
    Charles,
}

public sealed class SparkResult
{
    public required string OutputPath       { get; init; }
    public required int    ItemCount        { get; init; }
    public required InputFormat Format      { get; init; }
}

/// <summary>
/// Orchestrates the detect → parse → convert → write pipeline.
/// </summary>
public sealed class SparkPipeline
{
    private static readonly JsonSerializerOptions _readOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    private static readonly JsonSerializerOptions _writeOpts = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder                = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public SparkResult Run(SparkOptions options)
    {
        var json   = File.ReadAllText(options.InputPath);
        var name   = options.CollectionName
                     ?? Path.GetFileNameWithoutExtension(options.InputPath);
        var format = options.FormatOverride ?? Detect(options.InputPath, json);

        var collection = format == InputFormat.Charles
            ? ConvertCharles(json, name)
            : ConvertHar(json, name);

        var output = JsonSerializer.Serialize(collection, _writeOpts);
        var outPath = options.OutputPath;

        var dir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(outPath, output);

        return new SparkResult
        {
            OutputPath = outPath,
            ItemCount  = CountItems(collection.Item),
            Format     = format,
        };
    }

    // ------------------------------------------------------------------
    // Format detection
    // ------------------------------------------------------------------

    private static InputFormat Detect(string path, string json)
    {
        // .har extension → always HAR.
        if (string.Equals(Path.GetExtension(path), ".har", StringComparison.OrdinalIgnoreCase))
            return InputFormat.Har;

        // Peek at the JSON root keys.
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling  = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                // HAR: { "log": { "entries": [...] } }
                if (root.TryGetProperty("log", out _)) return InputFormat.Har;

                // Charles: { "sessions": [...] }
                if (root.TryGetProperty("sessions", out _)) return InputFormat.Charles;
            }
        }
        catch (JsonException) { /* fall through */ }

        throw new InvalidOperationException(
            $"Cannot determine input format for '{path}'. " +
            "Use --format har|charles to specify explicitly.");
    }

    // ------------------------------------------------------------------
    // Converters
    // ------------------------------------------------------------------

    private static PostmanCollection ConvertHar(string json, string name)
    {
        var file = JsonSerializer.Deserialize<HarFile>(json, _readOpts)
            ?? throw new InvalidOperationException("Failed to parse HAR file.");
        return new HarConverter().Convert(file.Log, name);
    }

    private static PostmanCollection ConvertCharles(string json, string name)
    {
        var root = JsonSerializer.Deserialize<CharlesRoot>(json, _readOpts)
            ?? throw new InvalidOperationException("Failed to parse Charles export.");
        return new CharlesConverter().Convert(root, name);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static int CountItems(List<PostmanItem> items)
    {
        var count = 0;
        foreach (var item in items)
        {
            if (item.Item is not null)
                count += CountItems(item.Item);
            else
                count++;
        }
        return count;
    }
}
