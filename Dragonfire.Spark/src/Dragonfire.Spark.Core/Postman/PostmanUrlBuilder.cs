namespace Dragonfire.Spark.Postman;

/// <summary>
/// Builds a <see cref="PostmanUrl"/> from a raw URL string. Splits into
/// protocol / host segments / path segments / query params so Postman renders
/// the URL correctly in its URL editor rather than as a raw opaque string.
/// </summary>
internal static class PostmanUrlBuilder
{
    internal static PostmanUrl Build(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            // Unparseable — store as raw only; Postman will still import it.
            return new PostmanUrl { Raw = rawUrl };
        }

        var url = new PostmanUrl
        {
            Raw      = rawUrl,
            Protocol = uri.Scheme,
            Host     = uri.Host.Split('.').ToList(),
            Path     = uri.AbsolutePath
                           .Split('/', StringSplitOptions.RemoveEmptyEntries)
                           .ToList(),
        };

        if (!string.IsNullOrEmpty(uri.Query))
        {
            url.Query = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair =>
                {
                    var eq  = pair.IndexOf('=');
                    var key = eq >= 0 ? Uri.UnescapeDataString(pair[..eq])      : pair;
                    var val = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..]) : "";
                    return new PostmanQueryParam { Key = key, Value = val };
                })
                .ToList();
        }

        return url;
    }

    /// <summary>
    /// Returns the URL with query string stripped (path only, for deduplication
    /// and operation naming).
    /// </summary>
    internal static string StripQuery(string rawUrl)
    {
        var q = rawUrl.IndexOf('?');
        return q >= 0 ? rawUrl[..q] : rawUrl;
    }
}
