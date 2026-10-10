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

        url = url.Trim();
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url[7..];
        }

        return WithSize(url, size);
    }

    [GeneratedRegex(@"^https?://covers\.openlibrary\.org/b/(?<kind>id|isbn|olid)/(?<id>[^/-]+)-[SML]\.jpg(\?.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenLibraryCover();
}
