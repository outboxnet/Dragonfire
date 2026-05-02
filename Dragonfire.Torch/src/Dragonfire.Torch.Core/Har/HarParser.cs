using System.Text.Json;
using Dragonfire.Torch.Inference;
using Dragonfire.Torch.Naming;
using Dragonfire.Torch.Postman;
using Dragonfire.Torch.Schema;

namespace Dragonfire.Torch.Har;

/// <summary>
/// Converts a parsed <see cref="HarLog"/> into a <see cref="ClientIR"/> using
/// the same inference and naming machinery as <see cref="PostmanParser"/>.
///
/// Strategy
/// --------
/// Each HAR entry becomes one operation. Operations are grouped by
/// (method, path-template) to detect duplicates — when the same endpoint
/// appears multiple times (e.g. a paginated response captured twice) only the
/// first occurrence is kept and the rest are dropped with a warning.
///
/// Path template extraction
/// -------------------------
/// HAR entries carry only the concrete URL, not a template. The parser
/// heuristically turns path segments that look like opaque IDs into {param}
/// placeholders using the same rules as the Postman parser:
///   • pure hex string (UUID-like, 24+ chars, 32 chars, etc.)
///   • all-digit segment
///   • mixed-alphanum with length >= 16 (likely a token / key)
/// The placeholder name is derived from the preceding segment
/// (e.g. /users/abc123 → /users/{userId}).
///
/// Base URL
/// --------
/// The override takes priority; otherwise the scheme+host of the first entry
/// is used and all other entries must share the same origin (cross-origin
/// entries are skipped with a warning).
/// </summary>
public sealed class HarParser
{
    private readonly ParseOptions _options;
    private readonly CollisionResolver _operationNames = new();
    private readonly List<TypeIR> _allTypes = new();

    private static readonly System.Text.RegularExpressions.Regex _guidLike =
        new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex _allDigits =
        new(@"^\d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Opaque alphanum strings >= 16 chars treated as ID-like path segments.
    private static readonly System.Text.RegularExpressions.Regex _opaqueId =
        new(@"^[0-9a-zA-Z_\-]{16,}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public HarParser(ParseOptions options) => _options = options;

    public ClientIR Parse(HarLog log)
    {
        var ir = new ClientIR
        {
            Namespace = _options.Namespace,
            ClientName = _options.ClientName,
        };

        var validEntries = log.Entries
            .Where(e => e.Request?.Url is not null)
            .ToList();

        if (validEntries.Count == 0)
        {
            ir.Warnings.Add("HAR file contains no request entries with a URL.");
            return ir;
        }

        // Determine base URL: override > first-entry origin.
        var baseUrl = ResolveBaseUrl(validEntries, ir.Warnings);
        ir.BaseUrl = baseUrl;

        // Filter to entries that match the base origin (skip cross-origin).
        var baseOrigin = GetOrigin(baseUrl);
        var sameOrigin = validEntries.Where(e =>
        {
            var origin = GetOrigin(e.Request!.Url!);
            if (string.IsNullOrEmpty(origin) || string.Equals(origin, baseOrigin, StringComparison.OrdinalIgnoreCase))
                return true;
            ir.Warnings.Add($"Skipping cross-origin entry: {e.Request!.Method} {e.Request!.Url}");
            return false;
        }).ToList();

        // Find common request headers (excluding Content-Type, Cookie, Accept, etc.)
        ir.CommonHeaders.AddRange(FindCommonHeaders(sameOrigin));
        var commonHeaderKeys = new HashSet<string>(
            ir.CommonHeaders.Select(h => h.Key), StringComparer.OrdinalIgnoreCase);

        var inferrer = new JsonSchemaInferrer(new InferenceOptions
        {
            FloatsAsDouble = _options.FloatsAsDouble,
        });

        // Dedup: track (METHOD, path-template) pairs already emitted.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in sameOrigin)
        {
            var op = BuildOperation(entry, baseOrigin, commonHeaderKeys, inferrer, ir.Warnings);
            if (op is null) continue;

            var key = $"{op.HttpMethod} {op.PathTemplate}";
            if (!seen.Add(key))
            {
                ir.Warnings.Add($"Duplicate entry for {key}; keeping first occurrence.");
                continue;
            }

            ir.Operations.Add(op);
        }

        _allTypes.AddRange(inferrer.CollectedTypes);
        BaseClassPromoter.Promote(_allTypes);
        ir.Types = _allTypes;

        return ir;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string ResolveBaseUrl(List<HarEntry> entries, List<string> warnings)
    {
        if (!string.IsNullOrEmpty(_options.BaseUrlOverride))
            return _options.BaseUrlOverride!;

        var first = entries[0].Request!.Url!;
        return GetOrigin(first);
    }

    private static string GetOrigin(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        return $"{uri.Scheme}://{uri.Authority}";
    }

    private static IEnumerable<HeaderIR> FindCommonHeaders(List<HarEntry> entries)
    {
        // Headers that are never useful as "common API headers".
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "content-type", "content-length", "host", "connection",
            "accept", "accept-encoding", "accept-language",
            "cookie", "user-agent", "referer", "origin",
            "sec-fetch-site", "sec-fetch-mode", "sec-fetch-dest",
            "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
            "cache-control", "pragma",
        };

        var perRequest = entries
            .Select(e => e.Request!.Headers
                .Where(h => !string.IsNullOrEmpty(h.Name) && !skip.Contains(h.Name!))
                .ToDictionary(h => h.Name!, h => h.Value ?? "", StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (perRequest.Count == 0) yield break;

        var first = perRequest[0];
        foreach (var (k, v) in first)
        {
            var allMatch = perRequest.All(d =>
                d.TryGetValue(k, out var x) && string.Equals(x, v, StringComparison.Ordinal));
            if (allMatch)
                yield return new HeaderIR { Key = k, Value = v, IsCommon = true };
        }
    }

    private OperationIR? BuildOperation(
        HarEntry entry,
        string baseOrigin,
        HashSet<string> commonHeaderKeys,
        JsonSchemaInferrer inferrer,
        List<string> warnings)
    {
        var req = entry.Request!;
        var method = (req.Method ?? "GET").ToUpperInvariant();

        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri))
        {
            warnings.Add($"Skipping entry with unparseable URL: {req.Url}");
            return null;
        }

        var (pathTemplate, pathParams) = BuildPath(uri);

        // Derive an operation name from the path segments.
        var opName = _operationNames.Reserve(DeriveOperationName(method, pathTemplate));

        var op = new OperationIR
        {
            Name = opName,
            HttpMethod = method,
            PathTemplate = pathTemplate,
        };
        op.PathParams.AddRange(pathParams);

        // Query params from the URL (HAR queryString field is canonical).
        foreach (var q in req.QueryString)
        {
            if (string.IsNullOrEmpty(q.Name)) continue;
            op.QueryParams.Add(new QueryParamIR
            {
                Name = IdentifierSanitizer.CamelCase(q.Name),
                JsonName = q.Name,
                CSharpType = "string?",
                IsNullable = true,
            });
        }

        // Extra (non-common, non-skip) headers.
        var skipHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "content-type", "content-length", "host", "connection",
            "accept", "accept-encoding", "accept-language",
            "cookie", "user-agent", "referer", "origin",
        };
        foreach (var h in req.Headers)
        {
            if (string.IsNullOrEmpty(h.Name)) continue;
            if (skipHeaders.Contains(h.Name!)) continue;
            if (commonHeaderKeys.Contains(h.Name!)) continue;
            op.ExtraHeaders.Add(new HeaderIR { Key = h.Name!, Value = h.Value ?? "" });
        }

        // Request body.
        var (reqBody, bodyKind) = BuildRequestBody(opName, req.PostData, inferrer, warnings);
        op.RequestBody = reqBody;
        op.BodyKind = bodyKind;

        // Response body — use the first 2xx response.
        op.ResponseBody = BuildResponseBody(opName, entry.Response, inferrer, warnings, out var statusCode);
        op.ExampleStatusCode = statusCode;

        return op;
    }

    private static (string Template, List<PathParamIR> Params) BuildPath(Uri uri)
    {
        var rawSegments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var segments = new List<string>();
        var @params = new List<PathParamIR>();

        for (var i = 0; i < rawSegments.Length; i++)
        {
            var seg = Uri.UnescapeDataString(rawSegments[i]);
            if (IsIdLike(seg))
            {
                // Name the placeholder after the previous segment if available.
                var prev = i > 0 ? rawSegments[i - 1] : "id";
                var paramName = IdentifierSanitizer.CamelCase(Singularize(prev)) + "Id";
                // Avoid collisions within this path.
                var unique = paramName;
                var n = 2;
                while (@params.Any(p => p.Name == unique)) unique = paramName + n++;
                @params.Add(new PathParamIR { Name = unique });
                segments.Add("{" + unique + "}");
            }
            else
            {
                segments.Add(seg);
            }
        }

        return ("/" + string.Join("/", segments), @params);
    }

    private static bool IsIdLike(string seg)
    {
        if (string.IsNullOrEmpty(seg)) return false;
        if (_guidLike.IsMatch(seg)) return true;
        if (_allDigits.IsMatch(seg)) return true;
        if (_opaqueId.IsMatch(seg)) return true;
        return false;
    }

    private static string Singularize(string word)
    {
        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            return word[..^3] + "y";
        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) && word.Length > 2)
            return word[..^1];
        return word;
    }

    private static string DeriveOperationName(string method, string pathTemplate)
    {
        // Build a name from the HTTP verb + meaningful path segments (skip {param} placeholders).
        var prefix = method switch
        {
            "GET"    => "Get",
            "POST"   => "Create",
            "PUT"    => "Update",
            "PATCH"  => "Patch",
            "DELETE" => "Delete",
            _        => IdentifierSanitizer.PascalCase(method),
        };

        var segments = pathTemplate
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith('{'))
            .Select(IdentifierSanitizer.PascalCase)
            .ToArray();

        var resource = segments.Length > 0
            ? string.Join("", segments)
            : "Resource";

        return prefix + resource;
    }

    private (TypeIR? Body, BodyKind Kind) BuildRequestBody(
        string opName,
        HarPostData? postData,
        JsonSchemaInferrer inferrer,
        List<string> warnings)
    {
        if (postData is null) return (null, BodyKind.None);
        var mime = postData.MimeType ?? "";

        if (mime.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var raw = postData.Text;
            if (string.IsNullOrWhiteSpace(raw)) return (null, BodyKind.None);
            try
            {
                var ir = inferrer.Infer($"{opName}Request", raw, warnings, TypeRole.Request);
                return (ir, BodyKind.Json);
            }
            catch (JsonException ex)
            {
                warnings.Add($"Operation '{opName}' has unparseable JSON body ({ex.Message}); request DTO not generated.");
                return (null, BodyKind.None);
            }
        }

        if (mime.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var fields = BuildUrlencodedFields(postData);
            if (fields.Count == 0) return (null, BodyKind.None);
            var ir = BuildFormType($"{opName}Request", fields, isMultipart: false);
            _allTypes.Add(ir);
            return (ir, BodyKind.FormUrlEncoded);
        }

        if (mime.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var fields = postData.Params
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .Select(p => (p.Name!, p.FileName, IsFile: !string.IsNullOrEmpty(p.FileName)))
                .ToList();

            if (fields.Count == 0) return (null, BodyKind.None);
            var ir = BuildMultipartType($"{opName}Request", fields);
            _allTypes.Add(ir);
            return (ir, BodyKind.FormData);
        }

        if (!string.IsNullOrEmpty(mime))
            warnings.Add($"Operation '{opName}' has unsupported body MIME type '{mime}'; request DTO not generated.");

        return (null, BodyKind.None);
    }

    private static List<(string Key, string? Value)> BuildUrlencodedFields(HarPostData postData)
    {
        // Prefer the structured params list; fall back to parsing the text field.
        if (postData.Params.Count > 0)
        {
            return postData.Params
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .Select(p => (p.Name!, p.Value))
                .ToList();
        }

        var text = postData.Text ?? "";
        if (string.IsNullOrWhiteSpace(text)) return new();

        return text.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var eq = pair.IndexOf('=');
                var key = eq >= 0 ? Uri.UnescapeDataString(pair[..eq].Replace('+', ' ')) : pair;
                var val = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')) : null;
                return (key, (string?)val);
            })
            .Where(t => !string.IsNullOrEmpty(t.key))
            .ToList();
    }

    private static TypeIR BuildFormType(string typeName, List<(string Key, string? Value)> fields, bool isMultipart)
    {
        var type = TypeIR.Class(typeName);
        type.Role = TypeRole.Request;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in fields)
        {
            var pascal = IdentifierSanitizer.PropertyName(key);
            var unique = pascal;
            var n = 2;
            while (!seen.Add(unique)) unique = pascal + "_" + n++;
            type.Properties.Add(new PropertyIR
            {
                Name = unique,
                JsonName = key,
                CSharpType = "string",
                IsNullable = false,
                IsString = true,
                IsCollection = false,
                IsFile = false,
            });
        }
        return type;
    }

    private static TypeIR BuildMultipartType(string typeName,
        List<(string Name, string? FileName, bool IsFile)> fields)
    {
        var type = TypeIR.Class(typeName);
        type.Role = TypeRole.Request;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _, isFile) in fields)
        {
            var pascal = IdentifierSanitizer.PropertyName(name);
            var unique = pascal;
            var n = 2;
            while (!seen.Add(unique)) unique = pascal + "_" + n++;
            type.Properties.Add(new PropertyIR
            {
                Name = unique,
                JsonName = name,
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
        HarResponse? response,
        JsonSchemaInferrer inferrer,
        List<string> warnings,
        out int? statusCode)
    {
        statusCode = null;

        if (response is null || response.Status < 200 || response.Status >= 300)
        {
            return EmitStub(opName, warnings, "no successful response in HAR entry");
        }

        statusCode = response.Status;
        var content = response.Content;

        if (content is null || string.IsNullOrWhiteSpace(content.Text))
            return EmitStub(opName, warnings, "empty response body");

        // Skip base64-encoded binary responses.
        if (string.Equals(content.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
            return EmitStub(opName, warnings, "binary (base64) response body");

        var mime = content.MimeType ?? "";
        if (!mime.Contains("json", StringComparison.OrdinalIgnoreCase))
            return EmitStub(opName, warnings, $"non-JSON response MIME type '{mime}'");

        try
        {
            return inferrer.Infer($"{opName}Response", content.Text, warnings, TypeRole.Response);
        }
        catch (JsonException ex)
        {
            return EmitStub(opName, warnings, $"response body is not JSON ({ex.Message})");
        }
    }

    private TypeIR EmitStub(string opName, List<string> warnings, string reason)
    {
        var stub = TypeIR.Class($"{opName}Response");
        stub.IsStub = true;
        stub.Role = TypeRole.Response;
        _allTypes.Add(stub);
        warnings.Add($"Operation '{opName}' has {reason}; emitted empty stub.");
        return stub;
    }
}
