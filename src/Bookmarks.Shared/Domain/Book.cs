namespace Bookmarks.Shared.Domain;

/// <summary>
/// Edition-level catalog metadata. Books from Open Library / Google Books are shared
/// across users (keyed by ISBN-13); manual entries are owned by the user that created them.
/// </summary>
public sealed class Book
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

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

    /// <summary>Set for <see cref="BookSource.Manual"/> entries.</summary>
    public Guid? CreatedByUserId { get; set; }

    public string PrimaryAuthor => Authors.Count > 0 ? Authors[0] : "";
}
