using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Lookup;

/// <summary>Book details found by a lookup service, ready to merge into the editor (<see cref="LookupMerge"/>).</summary>
public sealed record BookLookupResult
{
    public string? Isbn13 { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public string? Publisher { get; init; }

    public int? PublishedYear { get; init; }

    public int? PageCount { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public string? Series { get; init; }

    public decimal? SeriesNumber { get; init; }

    /// <summary>Medium-size cover (Open Library cover-id URL); see <see cref="CoverUrls"/> for other sizes.</summary>
    public string? CoverUrl { get; init; }

    public int? HeightMm { get; init; }

    public int? WidthMm { get; init; }

    public int? ThicknessMm { get; init; }

    /// <summary>Binding of this edition, when known. Applied to new books only (it describes "your copy").</summary>
    public BookFormat? Format { get; init; }

    public BookSource Source { get; init; }

    /// <summary>The source's own id, e.g. "/books/OL30036715M".</summary>
    public string? SourceKey { get; init; }
}

/// <summary>One title/author search result: a work, plus its best-matching edition when the source has one.</summary>
public sealed record BookSearchHit
{
    /// <summary>e.g. "/works/OL15358691W".</summary>
    public required string WorkKey { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public int? FirstPublishYear { get; init; }

    public string? CoverUrl { get; init; }

    public int EditionCount { get; init; }

    /// <summary>ISBN-13 of the best-matching edition. When set, picking this hit runs a full ISBN lookup.</summary>
    public string? Isbn13 { get; init; }

    public string? Publisher { get; init; }

    public int? PageCount { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public BookSource Source { get; init; } = BookSource.OpenLibrary;

    /// <summary>The hit as a lookup result, used when there's no ISBN or the ISBN lookup finds nothing.</summary>
    public BookLookupResult ToResult()
        => new()
        {
            Isbn13 = Isbn13,
            Title = Title,
            Subtitle = Subtitle,
            Authors = Authors,
            Publisher = Publisher,
            PublishedYear = FirstPublishYear,
            PageCount = PageCount,
            Genres = Genres,
            CoverUrl = CoverUrl,
            Source = Source,
            SourceKey = WorkKey,
        };
}
