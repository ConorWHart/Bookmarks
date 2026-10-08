using System.Text.Json.Serialization;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Shared.Domain;

/// <summary>
/// Edition-level metadata for a book in the user's library.
/// </summary>
/// <remarks>
/// User-owned and synced like every other record (Phase 4 decision): there is no shared catalog, so
/// editing a title only ever changes your own copy. Book lookups (Phase 5) fill in a new Book from
/// Open Library / Google Books rather than referencing a shared row; <see cref="Source"/> records where it came from.
/// </remarks>
public sealed class Book : SyncEntity
{
    public string? Isbn13 { get; set; }

    public string Title { get; set; } = "";

    public string? Subtitle { get; set; }

    public List<string> Authors { get; set; } = [];

    public string? Publisher { get; set; }

    public int? PublishedYear { get; set; }

    public int? PageCount { get; set; }

    public List<string> Genres { get; set; } = [];

    public string? Series { get; set; }

    public decimal? SeriesNumber { get; set; }

    public string? CoverUrl { get; set; }

    /// <summary>Physical dimensions when the source provides them.</summary>
    public int? HeightMm { get; set; }

    public int? WidthMm { get; set; }

    public int? ThicknessMm { get; set; }

    /// <summary>Dominant cover color (#RRGGBB), extracted once on the client.</summary>
    public string? DominantColor { get; set; }

    public BookSource Source { get; set; }

    [JsonIgnore]
    public string PrimaryAuthor => Authors.Count > 0 ? Authors[0] : "";
}
