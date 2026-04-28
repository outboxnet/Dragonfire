using System.Text.Json;
using Dragonfire.ApiClientGen.Inference;
using Dragonfire.ApiClientGen.Naming;
using Dragonfire.ApiClientGen.Schema;

namespace Dragonfire.ApiClientGen.Postman;

public sealed class ParseOptions
{
    public required string Namespace { get; init; }
    public required string ClientName { get; init; }
    public string? BaseUrlOverride { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? ResponseExamples { get; init; }
    public bool FloatsAsDouble { get; init; }
}

/// <summary>
/// Walks a parsed <see cref="PostmanCollection"/> and produces a fully
/// populated <see cref="ClientIR"/>. Folder hierarchy is flattened — folders
/// only carry display structure in Postman, not URL semantics.
/// </summary>
public sealed class PostmanParser
{
    private readonly ParseOptions _options;
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);
    private readonly CollisionResolver _operationNames = new();
    private readonly List<TypeIR> _allTypes = new();

    public PostmanParser(ParseOptions options) => _options = options;

    public ClientIR Parse(PostmanCollection collection)
    {
        var ir = new ClientIR
        {
            Namespace = _options.Namespace,
            ClientName = _options.ClientName,
        };

        // 1. Stash collection-level variables for later substitution.
        foreach (var v in collection.Variable)
        {
            if (!string.IsNullOrEmpty(v.Key)) _variables[v.Key!] = v.Value ?? "";
        }

        // 2. Flatten items.
        var items = new List<PostmanItem>();
        FlattenItems(collection.Item, items);

        var requestItems = items.Where(i => i.Request is not null).ToList();
        if (requestItems.Count == 0)
        {
            ir.Warnings.Add("Collection contains no request items.");
            return ir;
        }

        // 3. Determine base URL — explicit override > collection variable > first request.
        ir.BaseUrl = ResolveBaseUrl(requestItems);

        // 4. Determine common headers (present on EVERY request).
        ir.CommonHeaders.AddRange(FindCommonHeaders(requestItems));

        var commonHeaderKeys = new HashSet<string>(
            ir.CommonHeaders.Select(h => h.Key),
            StringComparer.OrdinalIgnoreCase);

        // 5. Build operations.
        var inferrer = new JsonSchemaInferrer(new InferenceOptions
        {
            FloatsAsDouble = _options.FloatsAsDouble,
        });

        foreach (var item in requestItems)
        {
            var op = BuildOperation(item, commonHeaderKeys, inferrer, ir.Warnings);
            if (op is not null) ir.Operations.Add(op);
        }

        // Collected types from inferrer go into the IR, plus we apply base-class
        // promotion to lift shared scalar pairs into BaseEntity.
        _allTypes.AddRange(inferrer.CollectedTypes);
        BaseClassPromoter.Promote(_allTypes);
        ir.Types = _allTypes;

        return ir;
    }

    private static void FlattenItems(IEnumerable<PostmanItem>? items, List<PostmanItem> sink)
    {
        if (items is null) return;
        foreach (var i in items)
        {
            if (i.IsFolder) FlattenItems(i.Item, sink);
            else sink.Add(i);
        }
    }

    private string ResolveBaseUrl(List<PostmanItem> items)
    {
        if (!string.IsNullOrEmpty(_options.BaseUrlOverride))
            return _options.BaseUrlOverride!;

        if (_variables.TryGetValue("baseUrl", out var v) && !string.IsNullOrEmpty(v))
            return v;

        // Fall back to the host of the first request, if it has one.
        var url = items[0].Request?.UrlObject;
        if (url is null) return "";
        if (url.Host.Count > 0) return string.Join(".", url.Host);
        if (!string.IsNullOrEmpty(url.Raw))
        {
            var raw = SubstituteVariables(url.Raw!);
            // Strip path: take scheme://host[:port]
            var idx = raw.IndexOf("://", StringComparison.Ordinal);
            if (idx > 0)
            {
                var slash = raw.IndexOf('/', idx + 3);
                return slash > 0 ? raw[..slash] : raw;
            }
        }
        return "";
    }

    private static IEnumerable<HeaderIR> FindCommonHeaders(List<PostmanItem> items)
    {
        // A header (key, value) is "common" iff every request has it with the
        // same value. Disabled headers are ignored.
        var perRequest = items
            .Select(i => i.Request!.Header
                .Where(h => !h.Disabled && !string.IsNullOrEmpty(h.Key))
                .ToDictionary(h => h.Key!, h => h.Value ?? "", StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (perRequest.Count == 0) yield break;

        var first = perRequest[0];
        foreach (var (k, v) in first)
        {
            // Content-Type is set automatically by JsonContent — don't promote
            // it to a "common header" or we'll double-set it on every request.
            if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;

            var allMatch = perRequest.All(d =>
                d.TryGetValue(k, out var x) && string.Equals(x, v, StringComparison.Ordinal));

            if (allMatch) yield return new HeaderIR { Key = k, Value = v, IsCommon = true };
        }
    }

    private OperationIR? BuildOperation(
        PostmanItem item,
        HashSet<string> commonHeaderKeys,
        JsonSchemaInferrer inferrer,
        List<string> warnings)
    {
        var request = item.Request!;
        var rawName = item.Name ?? "Unnamed";
        var pascal = IdentifierSanitizer.PascalCase(rawName);
        var opName = _operationNames.Reserve(pascal);

        var url = request.UrlObject;
        if (url is null)
        {
            warnings.Add($"Operation '{opName}' has no URL; skipping.");
            return null;
        }

        var op = new OperationIR
        {
            Name = opName,
            HttpMethod = (request.Method ?? "GET").ToUpperInvariant(),
        };

        // Path template + path parameters.
        var (pathTemplate, pathParams) = BuildPath(url);
        op.PathTemplate = pathTemplate;
        op.PathParams.AddRange(pathParams);

        // Query parameters (only enabled ones).
        foreach (var q in url.Query)
        {
            if (q.Disabled || string.IsNullOrEmpty(q.Key)) continue;
            op.QueryParams.Add(new QueryParamIR
            {
                Name = IdentifierSanitizer.CamelCase(q.Key!),
                JsonName = q.Key!,
                CSharpType = "string?",
                IsNullable = true,
            });
        }

        // Headers — drop common ones.
        foreach (var h in request.Header)
        {
            if (h.Disabled || string.IsNullOrEmpty(h.Key)) continue;
            if (commonHeaderKeys.Contains(h.Key!)) continue;
            if (h.Key!.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            op.ExtraHeaders.Add(new HeaderIR { Key = h.Key!, Value = h.Value ?? "" });
        }

        // Request body.
        var (body, kind) = BuildRequestBody(opName, request.Body, inferrer, warnings);
        op.RequestBody = body;
        op.BodyKind = kind;

        // Response body — pick the first 2xx example, falling back to override.
        op.ResponseBody = BuildResponseBody(opName, item.Response, inferrer, warnings, out var statusCode);
        op.ExampleStatusCode = statusCode;

        return op;
    }

    private (string Template, List<PathParamIR> Params) BuildPath(PostmanUrl url)
    {
        var segments = new List<string>();
        var paramList = new List<PathParamIR>();

        foreach (var seg in url.PathSegments())
        {
            var canonical = NormalisePathSegment(seg, paramList);
            segments.Add(canonical);
        }

        var template = "/" + string.Join("/", segments);

        // Path-variable hints from url.variable[] override the inferred type
        // (Postman only — the value field can give us a sniffable example).
        foreach (var hint in url.Variable)
        {
            if (string.IsNullOrEmpty(hint.Key)) continue;
            var existing = paramList.FirstOrDefault(p => p.Name == hint.Key);
            if (existing is null) continue;
            // Default stays "string"; we could sniff hint.Value here for Guid /
            // int but it's rarely useful and surprises the consumer.
        }

        return (template, paramList);
    }

    private static string NormalisePathSegment(string seg, List<PathParamIR> paramList)
    {
        // ":id" form
        if (seg.StartsWith(':'))
        {
            var name = seg[1..];
            paramList.Add(new PathParamIR { Name = name });
            return "{" + name + "}";
        }
        // "{{id}}" form (a Postman variable used as a path param)
        if (seg.StartsWith("{{") && seg.EndsWith("}}"))
        {
            var name = seg[2..^2];
            paramList.Add(new PathParamIR { Name = name });
            return "{" + name + "}";
        }
        return seg;
    }

    private (TypeIR? Body, BodyKind Kind) BuildRequestBody(
        string opName,
        PostmanBody? body,
        JsonSchemaInferrer inferrer,
        List<string> warnings)
    {
        if (body is null) return (null, BodyKind.None);
        var mode = body.Mode ?? "";

        if (string.Equals(mode, "raw", StringComparison.OrdinalIgnoreCase))
        {
            var raw = body.Raw;
            if (string.IsNullOrWhiteSpace(raw)) return (null, BodyKind.None);

            var substituted = SubstituteVariables(raw!);
            try
            {
                var ir = inferrer.Infer($"{opName}Request", substituted, warnings, TypeRole.Request);
                return (ir, BodyKind.Json);
            }
            catch (JsonException ex)
            {
                warnings.Add($"Operation '{opName}' has unparseable raw body ({ex.Message}); request DTO not generated.");
                return (null, BodyKind.None);
            }
        }

        if (string.Equals(mode, "urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var ir = BuildFormType($"{opName}Request", body.UrlEncoded, isMultipart: false);
            if (ir is null) return (null, BodyKind.None);
            _allTypes.Add(ir);
            return (ir, BodyKind.FormUrlEncoded);
        }

        if (string.Equals(mode, "formdata", StringComparison.OrdinalIgnoreCase))
        {
            var ir = BuildFormType($"{opName}Request", body.FormData, isMultipart: true);
            if (ir is null) return (null, BodyKind.None);
            _allTypes.Add(ir);
            return (ir, BodyKind.FormData);
        }

        if (!string.IsNullOrEmpty(mode))
            warnings.Add($"Operation '{opName}' has unsupported body mode '{mode}'; request DTO not generated.");
        return (null, BodyKind.None);
    }

    /// <summary>
    /// Builds a request DTO from a flat list of form fields. For
    /// <c>multipart/form-data</c> fields where <c>type == "file"</c> we render
    /// the property as <c>Stream?</c> so the emitter can wrap it in
    /// <see cref="System.Net.Http.StreamContent"/> at send time. All other
    /// fields are plain strings.
    /// </summary>
    private static TypeIR? BuildFormType(string typeName, List<PostmanFormField> fields, bool isMultipart)
    {
        var enabled = fields
            .Where(f => !string.IsNullOrEmpty(f.Key))
            .ToList();
        if (enabled.Count == 0) return null;

        var type = TypeIR.Class(typeName);
        type.Role = TypeRole.Request;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in enabled)
        {
            var pascal = IdentifierSanitizer.PropertyName(f.Key!);
            // Resolve duplicate field names within the same form by suffixing.
            var unique = pascal;
            var n = 2;
            while (!seen.Add(unique)) unique = pascal + "_" + n++;

            var isFile = isMultipart && string.Equals(f.Type, "file", StringComparison.OrdinalIgnoreCase);
            type.Properties.Add(new PropertyIR
            {
                Name = unique,
                JsonName = f.Key!,
                CSharpType = isFile ? "Stream?" : "string",
                IsNullable = isFile,
                IsString = !isFile,
                IsCollection = false,
                IsFile = isFile,
            });
        }
        return type;
    }

    private TypeIR? BuildResponseBody(
        string opName,
        List<PostmanResponse> responses,
        JsonSchemaInferrer inferrer,
        List<string> warnings,
        out int? statusCode)
    {
        statusCode = null;

        // 1. Try saved examples — first 2xx wins.
        var primary = responses
            .Where(r => r.Code is >= 200 and < 300 && !string.IsNullOrWhiteSpace(r.Body))
            .FirstOrDefault();

        string? bodyJson = null;
        if (primary is not null)
        {
            bodyJson = primary.Body;
            statusCode = primary.Code;
        }

        // 2. Try override file (keyed by operation name OR PascalCase fallback).
        if (bodyJson is null && _options.ResponseExamples is not null)
        {
            if (_options.ResponseExamples.TryGetValue(opName, out var el) ||
                _options.ResponseExamples.TryGetValue(opName + "Async", out el))
            {
                bodyJson = el.GetRawText();
            }
        }

        if (string.IsNullOrWhiteSpace(bodyJson))
        {
            // Stub: emit empty class so callers compile and can fill in.
            var stub = TypeIR.Class($"{opName}Response");
            stub.IsStub = true;
            stub.Role = TypeRole.Response;
            _allTypes.Add(stub);
            warnings.Add($"Operation '{opName}' has no example response; emitted empty stub.");
            return stub;
        }

        try
        {
            return inferrer.Infer($"{opName}Response", SubstituteVariables(bodyJson!), warnings, TypeRole.Response);
        }
        catch (JsonException ex)
        {
            warnings.Add($"Operation '{opName}' response example is not JSON ({ex.Message}); emitted empty stub.");
            var stub = TypeIR.Class($"{opName}Response");
            stub.IsStub = true;
            stub.Role = TypeRole.Response;
            _allTypes.Add(stub);
            return stub;
        }
    }

    private string SubstituteVariables(string raw)
    {
        if (raw.IndexOf("{{", StringComparison.Ordinal) < 0) return raw;
        var output = raw;
        foreach (var (k, v) in _variables)
        {
            output = output.Replace("{{" + k + "}}", v);
        }
        return output;
    }
}
