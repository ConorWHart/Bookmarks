using System.Text.RegularExpressions;

namespace Bookmarks.Core.Lookup;

/// <summary>Open Library cover sizes: S ≈ 45 px wide, M ≈ 180 px, L ≈ 500 px.</summary>
public enum CoverSize
{
    Small = 'S',
    Medium = 'M',
    Large = 'L',
}

/// <summary>
/// Builds and resizes cover URLs. We always store Open Library covers by cover id: covers by ISBN are
/// rate-limited (100 per 5 minutes per IP) while covers by id are not.
/// </summary>
public static partial class CoverUrls
{
    public const string OpenLibraryHost = "covers.openlibrary.org";

    public static string ForOpenLibraryId(long coverId, CoverSize size = CoverSize.Medium)
        => FormattableString.Invariant($"https://{OpenLibraryHost}/b/id/{coverId}-{(char)size}.jpg");

    /// <summary>
    /// The same Open Library cover at another size (also upgrades http to https). Other URLs are returned unchanged.
    /// </summary>
    public static string WithSize(string url, CoverSize size)
    {
        ArgumentNullException.ThrowIfNull(url);
        var m = OpenLibraryCover().Match(url);
        return m.Success
            ? $"https://{OpenLibraryHost}/b/{m.Groups["kind"].Value}/{m.Groups["id"].Value}-{(char)size}.jpg"
            : url;
    }

    public static bool IsOpenLibrary(string? url) => url is not null && OpenLibraryCover().IsMatch(url);

    /// <summary>Normalizes a cover URL from a lookup: https, and our preferred size for Open Library covers.</summary>
    public static string? Normalize(string? url, CoverSize size = CoverSize.Medium)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        url = Unwrap(url.Trim());
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url[7..];
        }

        return WithSize(url, size);
    }

    /// <summary>A cover the user pasted ("Copy image"), kept with the book as a <c>data:image/…</c> URL.</summary>
    public static bool IsPasted(string? url) => url is not null && url.StartsWith("data:image/", StringComparison.Ordinal);

    /// <summary>
    /// A link to a search results page rather than to an image: what Chrome on a phone gives for "Share image" on a
    /// Google image result (<c>google.com/search?…#sv=…</c> or a <c>share.google</c> short link). Such a link holds only
    /// Google's own ids for the result, so the image can't be found from it; the user has to copy the image instead.
    /// </summary>
    public static bool IsSearchPage(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();
        return host is "share.google" or "goo.gl"
            || (GoogleHost().IsMatch(host) && (path.StartsWith("/search", StringComparison.Ordinal) || path is "/imgres" or "/url"))
            || (BingHost().IsMatch(host) && path.StartsWith("/images/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The image inside a search engine's link to it, where the link carries it: Google's <c>/imgres?imgurl=…</c> and
    /// <c>/url?url=…</c>, Bing's <c>/images/search?…mediaurl=…</c>. Anything else is returned unchanged.
    /// </summary>
    public static string Unwrap(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Query))
        {
            return url;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();
        string[] keys = GoogleHost().IsMatch(host) && path == "/imgres" ? ["imgurl"]
            : GoogleHost().IsMatch(host) && path == "/url" ? ["url", "q"]
            : BingHost().IsMatch(host) && path.StartsWith("/images/", StringComparison.Ordinal) ? ["mediaurl"]
            : [];
        foreach (var key in keys)
        {
            if (QueryValue(uri.Query, key) is { } inner
                && Uri.TryCreate(inner, UriKind.Absolute, out var innerUri)
                && (innerUri.Scheme == Uri.UriSchemeHttps || innerUri.Scheme == Uri.UriSchemeHttp))
            {
                return inner;
            }
        }

        return url;
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && string.Equals(part[..eq], key, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' ')).Trim();
                }
                catch (UriFormatException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"^(www\.|images\.|m\.)?google\.[a-z]{2,3}(\.[a-z]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex GoogleHost();

    [GeneratedRegex(@"^(www\.)?bing\.com$", RegexOptions.CultureInvariant)]
    private static partial Regex BingHost();

    [GeneratedRegex(@"^https?://covers\.openlibrary\.org/b/(?<kind>id|isbn|olid)/(?<id>[^/-]+)-[SML]\.jpg(\?.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenLibraryCover();
}
