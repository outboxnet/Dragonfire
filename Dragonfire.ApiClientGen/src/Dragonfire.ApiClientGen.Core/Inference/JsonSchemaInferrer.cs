using System.Text.Json;
using Dragonfire.ApiClientGen.Naming;
using Dragonfire.ApiClientGen.Schema;

namespace Dragonfire.ApiClientGen.Inference;

public sealed class InferenceOptions
{
    public bool FloatsAsDouble { get; init; }
}

/// <summary>
/// Walks a parsed JSON example and produces a <see cref="TypeIR"/> tree.
/// Hoists nested objects into sibling types named <c>{Parent}{Property}</c> and
/// registers them with the supplied collector so the emitter can produce one
/// file per type.
/// </summary>
public sealed class JsonSchemaInferrer
{
    private readonly InferenceOptions _options;
    private readonly List<TypeIR> _collected;
    private readonly Dictionary<string, TypeIR> _byName;

    public JsonSchemaInferrer(InferenceOptions? options = null)
    {
        _options = options ?? new InferenceOptions();
        _collected = new List<TypeIR>();
        _byName = new Dictionary<string, TypeIR>(StringComparer.Ordinal);
    }

    public IReadOnlyList<TypeIR> CollectedTypes => _collected;

    /// <summary>
    /// Top-level entry point. Returns a <see cref="TypeIR"/> for the root of
    /// <paramref name="json"/>; nested objects are registered as sibling types
    /// reachable via <see cref="CollectedTypes"/>.
    /// </summary>
    public TypeIR Infer(string typeName, string json, List<string>? warnings = null, TypeRole role = TypeRole.Other)
    {
        using var doc = JsonDocument.Parse(json);
        var startCount = _collected.Count;
        var ir = InferElement(typeName, doc.RootElement, warnings ?? new List<string>())
               ?? throw new InvalidOperationException("Root element produced no type.");

        // Tag every newly-collected class (root + nested) with the supplied role.
        for (var i = startCount; i < _collected.Count; i++) _collected[i].Role = role;
        if (ir.IsClass) ir.Role = role;
        return ir;
    }

    public TypeIR InferElement(string typeName, JsonElement el, List<string> warnings)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                return InferObject(typeName, el, warnings);

            case JsonValueKind.Array:
            {
                var elemType = InferArrayElement(typeName, el, warnings);
                return TypeIR.Primitive($"List<{elemType}>");
            }

            default:
                return TypeIR.Primitive(PrimitiveSniffer.SniffScalar(el, _options.FloatsAsDouble));
        }
    }

    private TypeIR InferObject(string typeName, JsonElement el, List<string> warnings)
    {
        var name = ReserveName(typeName);
        var type = TypeIR.Class(name);
        _byName[name] = type;
        _collected.Add(type);

        foreach (var prop in el.EnumerateObject())
        {
            var propName = IdentifierSanitizer.PropertyName(prop.Name);
            var p = new PropertyIR
            {
                Name = propName,
                JsonName = prop.Name,
            };

            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    var nested = InferObject(name + propName, prop.Value, warnings);
                    p.CSharpType = nested.Name;
                    break;
                }
                case JsonValueKind.Array:
                {
                    var elementCsharp = InferArrayElement(name + propName, prop.Value, warnings);
                    p.CSharpType = $"List<{elementCsharp}>";
                    p.IsCollection = true;
                    break;
                }
                case JsonValueKind.Null:
                    p.CSharpType = "string?";
                    p.IsNullable = true;
                    break;
                default:
                {
                    var csharp = PrimitiveSniffer.SniffScalar(prop.Value, _options.FloatsAsDouble);
                    p.CSharpType = csharp;
                    p.IsString = csharp == "string";
                    break;
                }
            }

            type.Properties.Add(p);
        }

        return type;
    }

    private string InferArrayElement(string parentName, JsonElement arrayEl, List<string> warnings)
    {
        var elements = arrayEl.EnumerateArray().ToList();
        if (elements.Count == 0)
        {
            warnings.Add($"Array '{parentName}' has no example elements; defaulting to List<string>.");
            return "string";
        }

        var first = elements[0];
        switch (first.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var elemName = Singularize(parentName);
                var nested = InferObject(elemName, first, warnings);

                // Cross-check siblings: same kind only.
                for (var i = 1; i < elements.Count; i++)
                {
                    if (elements[i].ValueKind != JsonValueKind.Object)
                    {
                        warnings.Add($"Heterogeneous array '{parentName}'; falling back to List<JsonElement>.");
                        return "System.Text.Json.JsonElement";
                    }
                }

                return nested.Name;
            }
            case JsonValueKind.Array:
                // Nested arrays are rare and not worth special inference.
                warnings.Add($"Nested array inside '{parentName}'; falling back to List<JsonElement>.");
                return "System.Text.Json.JsonElement";

            default:
            {
                var firstType = PrimitiveSniffer.SniffScalar(first, _options.FloatsAsDouble);
                for (var i = 1; i < elements.Count; i++)
                {
                    var t = PrimitiveSniffer.SniffScalar(elements[i], _options.FloatsAsDouble);
                    if (t != firstType)
                    {
                        warnings.Add($"Heterogeneous primitive array '{parentName}'; falling back to List<JsonElement>.");
                        return "System.Text.Json.JsonElement";
                    }
                }
                return firstType;
            }
        }
    }

    /// <summary>
    /// Cheap singularizer: trims a trailing "s" or "es". Good enough for the
    /// vast majority of JSON list field names ("items", "tenants", "invoices").
    /// </summary>
    private static string Singularize(string name)
    {
        if (name.EndsWith("ies", StringComparison.Ordinal) && name.Length > 3)
            return name[..^3] + "y";
        if (name.EndsWith("s", StringComparison.Ordinal) && name.Length > 1)
            return name[..^1];
        return name + "Item";
    }

    private string ReserveName(string desired)
    {
        if (!_byName.ContainsKey(desired)) return desired;

        var i = 2;
        while (_byName.ContainsKey($"{desired}_{i}")) i++;
        return $"{desired}_{i}";
    }
}
