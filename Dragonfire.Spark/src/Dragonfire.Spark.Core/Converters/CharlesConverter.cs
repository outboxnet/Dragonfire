using Dragonfire.Spark.Charles;
using Dragonfire.Spark.Postman;

namespace Dragonfire.Spark.Converters;

/// <summary>
/// Converts a Charles Proxy JSON export to a Postman v2.1 collection.
/// The Charles session tree is preserved as Postman folders: each named
/// <see cref="CharlesSession"/> (with sub-sessions or transactions) becomes a
/// folder. Unnamed sessions are inlined into their parent.
/// </summary>
public sealed class CharlesConverter
{
    private static readonly HashSet<string> _skipHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "connection", "host", "proxy-connection",
        "accept-encoding", "accept-language",
        "sec-fetch-site", "sec-fetch-mode", "sec-fetch-dest",
        "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
        "upgrade-insecure-requests", "dnt", "pragma",
        "user-agent",
    };

    public PostmanCollection Convert(CharlesRoot root, string collectionName)
    {
        var collection = new PostmanCollection
        {
            Info = new PostmanInfo { Name = collectionName },
        };

        foreach (var session in root.Sessions)
            collection.Item.AddRange(ConvertSession(session));

        return collection;
    }

    private static List<PostmanItem> ConvertSession(CharlesSession session)
    {
        var items = new List<PostmanItem>();

        // Convert direct transactions.
        foreach (var tx in session.Transactions)
        {
            var item = ConvertTransaction(tx);
            if (item is not null) items.Add(item);
        }

        // Recurse into sub-sessions.
        foreach (var sub in session.Sessions)
            items.AddRange(ConvertSession(sub));

        // If this session has a meaningful name, wrap in a folder.
        var name = session.Name?.Trim();
        if (!string.IsNullOrEmpty(name) && items.Count > 0)
        {
            return new List<PostmanItem>
            {
                new PostmanItem { Name = name, Item = items }
            };
        }

        return items;
    }

    private static PostmanItem? ConvertTransaction(CharlesTransaction tx)
    {
        if (tx.Request?.Url is null) return null;

        var method = (tx.Request.Method ?? "GET").ToUpperInvariant();
        var name   = DeriveName(method, tx.Request.Url);
        var req    = BuildRequest(tx.Request);

        var item = new PostmanItem
        {
            Name    = name,
            Request = req,
        };

        if (tx.Response is not null)
        {
            var saved = BuildSavedResponse(name, req, tx.Response);
            if (saved is not null)
                item.Response = new List<PostmanSavedResponse> { saved };
        }

        return item;
    }

    private static PostmanRequest BuildRequest(CharlesRequest req)
    {
        var postmanReq = new PostmanRequest
        {
            Method = (req.Method ?? "GET").ToUpperInvariant(),
            Url    = PostmanUrlBuilder.Build(req.Url!),
            Header = (req.Headers?.Headers ?? new())
                .Where(h => !string.IsNullOrEmpty(h.Name) && !_skipHeaders.Contains(h.Name!))
                .Select(h => new PostmanHeader { Key = h.Name!, Value = h.Value ?? "" })
                .ToList(),
        };

        if (req.Body is not null && !req.Body.IsBinary)
            postmanReq.Body = BuildBody(req.Body);

        return postmanReq;
    }

    private static PostmanBody? BuildBody(CharlesBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Text)) return null;

        var mime = body.Mime ?? "";

        if (mime.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return new PostmanBody
            {
                Mode = "raw",
                Raw  = body.Text,
                Options = new PostmanBodyOptions
                {
                    Raw = new PostmanRawOptions { Language = "json" }
                },
            };
        }

        if (mime.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            var fields = body.Text
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair =>
                {
                    var eq  = pair.IndexOf('=');
                    var key = eq >= 0 ? Uri.UnescapeDataString(pair[..eq].Replace('+', ' '))      : pair;
                    var val = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')) : null;
                    return new PostmanFormParam { Key = key, Value = val, Type = "text" };
                })
                .Where(p => !string.IsNullOrEmpty(p.Key))
                .ToList();

            return new PostmanBody { Mode = "urlencoded", UrlEncoded = fields };
        }

        // Fallback — preserve body as raw text.
        return new PostmanBody { Mode = "raw", Raw = body.Text };
    }

    private static PostmanSavedResponse? BuildSavedResponse(
        string name, PostmanRequest originalReq, CharlesResponse resp)
    {
        if (resp.Body?.IsBinary == true) return null;

        return new PostmanSavedResponse
        {
            Name            = $"{resp.Status} {resp.Message ?? ""}".Trim(),
            OriginalRequest = originalReq,
            Status          = resp.Message ?? "",
            Code            = resp.Status,
            Header          = (resp.Headers?.Headers ?? new())
                .Where(h => !string.IsNullOrEmpty(h.Name))
                .Select(h => new PostmanHeader { Key = h.Name!, Value = h.Value ?? "" })
                .ToList(),
            Body = resp.Body?.Text,
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
