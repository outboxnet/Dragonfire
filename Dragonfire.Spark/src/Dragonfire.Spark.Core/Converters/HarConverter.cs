using Dragonfire.Spark.Har;
using Dragonfire.Spark.Postman;

namespace Dragonfire.Spark.Converters;

/// <summary>
/// Converts a <see cref="HarLog"/> to a flat Postman v2.1 collection.
/// Each HAR entry becomes one Postman request item with its response attached
/// as a saved example. Duplicate <c>(method, url-without-query)</c> pairs are
/// collapsed: the first example wins and subsequent ones are appended as
/// additional saved responses on the same item.
/// </summary>
public sealed class HarConverter
{
    // Headers that add noise to every request and are not useful in a Postman
    // collection (browser plumbing, not API-level concerns).
    private static readonly HashSet<string> _skipHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept-encoding", "accept-language", "connection", "host",
        "sec-fetch-site", "sec-fetch-mode", "sec-fetch-dest",
        "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
        "upgrade-insecure-requests", "dnt", "pragma", "cache-control",
        "user-agent", "referer", "origin",
    };

    public PostmanCollection Convert(HarLog log, string collectionName)
    {
        var collection = new PostmanCollection
        {
            Info = new PostmanInfo { Name = collectionName },
        };

        // (method, path) → PostmanItem already added, for deduplication.
        var index = new Dictionary<string, PostmanItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in log.Entries)
        {
            if (entry.Request?.Url is null) continue;

            var method    = (entry.Request.Method ?? "GET").ToUpperInvariant();
            var url       = entry.Request.Url;
            var dedupeKey = $"{method} {PostmanUrlBuilder.StripQuery(url)}";
            var name      = DeriveName(method, url);

            var request = BuildRequest(entry.Request);

            if (!index.TryGetValue(dedupeKey, out var item))
            {
                item = new PostmanItem
                {
                    Name     = name,
                    Request  = request,
                    Response = new List<PostmanSavedResponse>(),
                };
                collection.Item.Add(item);
                index[dedupeKey] = item;
            }

            if (entry.Response is not null)
            {
                var saved = BuildSavedResponse(name, request, entry.Response, item.Response!.Count);
                if (saved is not null) item.Response!.Add(saved);
            }
        }

        return collection;
    }

    private static PostmanRequest BuildRequest(HarRequest req)
    {
        var postmanReq = new PostmanRequest
        {
            Method = (req.Method ?? "GET").ToUpperInvariant(),
            Url    = PostmanUrlBuilder.Build(req.Url!),
            Header = req.Headers
                .Where(h => !string.IsNullOrEmpty(h.Name) && !_skipHeaders.Contains(h.Name!))
                .Select(h => new PostmanHeader { Key = h.Name!, Value = h.Value ?? "" })
                .ToList(),
        };

        if (req.PostData is not null)
            postmanReq.Body = BuildBody(req.PostData);

        return postmanReq;
    }

    private static PostmanBody? BuildBody(HarPostData data)
    {
        var mime = data.MimeType ?? "";

        if (mime.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return new PostmanBody
            {
                Mode = "raw",
                Raw  = data.Text,
                Options = new PostmanBodyOptions
                {
                    Raw = new PostmanRawOptions { Language = "json" }
                },
            };
        }

        if (mime.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var fields = data.Params.Count > 0
                ? data.Params
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .Select(p => new PostmanFormParam { Key = p.Name!, Value = p.Value, Type = "text" })
                    .ToList()
                : ParseUrlencodedText(data.Text);

            return new PostmanBody { Mode = "urlencoded", UrlEncoded = fields };
        }

        if (mime.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var fields = data.Params
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .Select(p => new PostmanFormParam
                {
                    Key  = p.Name!,
                    Value = string.IsNullOrEmpty(p.FileName) ? p.Value : null,
                    Type  = string.IsNullOrEmpty(p.FileName) ? "text" : "file",
                    Src   = p.FileName,
                })
                .ToList();

            return new PostmanBody { Mode = "formdata", FormData = fields };
        }

        // Fallback — store as raw text regardless of MIME type.
        if (!string.IsNullOrWhiteSpace(data.Text))
            return new PostmanBody { Mode = "raw", Raw = data.Text };

        return null;
    }

    private static List<PostmanFormParam> ParseUrlencodedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        return text.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var eq  = pair.IndexOf('=');
                var key = eq >= 0 ? Uri.UnescapeDataString(pair[..eq].Replace('+', ' '))      : pair;
                var val = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')) : null;
                return new PostmanFormParam { Key = key, Value = val, Type = "text" };
            })
            .Where(p => !string.IsNullOrEmpty(p.Key))
            .ToList();
    }

    private static PostmanSavedResponse? BuildSavedResponse(
        string name, PostmanRequest originalReq, HarResponse resp, int index)
    {
        var label = index == 0
            ? $"{resp.Status} {resp.StatusText ?? ""}".Trim()
            : $"{resp.Status} {resp.StatusText ?? ""} ({index + 1})".Trim();

        // Skip binary responses.
        var encoding = resp.Content?.Encoding ?? "";
        if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
            return null;

        return new PostmanSavedResponse
        {
            Name            = label,
            OriginalRequest = originalReq,
            Status          = resp.StatusText ?? "",
            Code            = resp.Status,
            Header          = resp.Headers
                .Where(h => !string.IsNullOrEmpty(h.Name))
                .Select(h => new PostmanHeader { Key = h.Name!, Value = h.Value ?? "" })
                .ToList(),
            Body = resp.Content?.Text,
        };
    }

    private static string DeriveName(string method, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"{method} {url}";

        var path = uri.AbsolutePath.TrimEnd('/');
        return string.IsNullOrEmpty(path) || path == "/"
            ? $"{method} /"
            : $"{method} {path}";
    }
}
