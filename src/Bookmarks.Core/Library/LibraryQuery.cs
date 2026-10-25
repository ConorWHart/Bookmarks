using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

public enum ReadState
{
    Any = 0,
    Read = 1,
    Unread = 2,
    Reading = 3,

    /// <summary>
    /// Owned or lent out and not being read: never finished, or finished and marked "Want to re-read".
    /// The "to be read" pile.
    /// </summary>
    ToRead = 4,
}

public enum LibrarySortField
{
    ShelfOrder = 0,
    Title = 1,
    Author = 2,
    Series = 3,
    DateAdded = 4,
    Rating = 5,
    Height = 6,
    Color = 7,
    ReadCount = 8,

    /// <summary>Most recent finished read (Phase 6d).</summary>
    LastRead = 9,
}

public enum LibraryGroupBy
{
    None = 0,
    Author = 1,
    Genre = 2,
    Status = 3,
    Series = 4,
    Format = 5,
}

/// <summary>The two ways to see the library (Phase 6b).</summary>
public enum LibraryDisplay
{
    /// <summary>The user's bookcases, books in their shelf order; filters dim, sort and grouping don't apply.</summary>
    Bookcases = 0,

    /// <summary>Every book, filtered, sorted and grouped, laid out as <see cref="BooksLayout"/>.</summary>
    Books = 1,
}

/// <summary>How the Books display draws its books (Phase 6d).</summary>
public enum BooksLayout
{
    /// <summary>A grid of covers.</summary>
    Covers = 0,

    /// <summary>A compact list, one row per book.</summary>
    List = 1,
}

/// <summary>Date ranges for filters. Relative windows stay current when a preset is reopened later.</summary>
public enum DateWindow
{
    Any = 0,
    Last30Days = 1,
    Last90Days = 2,
    ThisYear = 3,
    LastYear = 4,
    Custom = 5,
}

/// <summary>A date filter. <see cref="From"/>/<see cref="To"/> (inclusive) only apply to <see cref="DateWindow.Custom"/>.</summary>
public sealed record DateFilter(DateWindow Window, DateOnly? From = null, DateOnly? To = null)
{
    public bool IsActive => Window switch
    {
        DateWindow.Any => false,
        DateWindow.Custom => From is not null || To is not null,
        _ => true,
    };

    /// <summary>The inclusive range this filter means on <paramref name="today"/> (null = open-ended).</summary>
    public (DateOnly? From, DateOnly? To) Resolve(DateOnly today) => Window switch
    {
        DateWindow.Last30Days => (today.AddDays(-29), today),
        DateWindow.Last90Days => (today.AddDays(-89), today),
        DateWindow.ThisYear => (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
        DateWindow.LastYear => (new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31)),
        DateWindow.Custom => (From, To),
        _ => (null, null),
    };

    /// <summary>Inactive filters match everything; active ones never match a missing date.</summary>
    public bool Matches(DateOnly? date, DateOnly today)
    {
        if (!IsActive)
        {
            return true;
        }

        if (date is not { } d)
        {
            return false;
        }

        var (from, to) = Resolve(today);
        return (from is null || d >= from) && (to is null || d <= to);
    }
}

/// <summary>
/// Filter criteria. Null / empty means "don't filter on this". Serialized (through <c>LibraryViewJson</c>'s
/// DTO), so add properties rather than renaming them.
/// </summary>
public sealed record LibraryFilter
{
    /// <summary><see cref="BookcaseId"/> value meaning "books not on any shelf".</summary>
    public static readonly Guid Unshelved = Guid.Empty;

    public string? Search { get; init; }

    public IReadOnlySet<OwnershipStatus>? Statuses { get; init; }

    public IReadOnlySet<BookFormat>? Formats { get; init; }

    public ReadState ReadState { get; init; }

    public decimal? MinRating { get; init; }

    /// <summary>Finished at least this many times.</summary>
    public int? MinReadCount { get; init; }

    /// <summary>Books on this shelf.</summary>
    public Guid? ShelfId { get; init; }

    /// <summary>Books on a shelf of this bookcase; <see cref="Unshelved"/> = books on no shelf at all.</summary>
    public Guid? BookcaseId { get; init; }

    /// <summary>Any of the book's authors (case-insensitive).</summary>
    public IReadOnlySet<string>? Authors { get; init; }

    /// <summary>Any of the book's genres (case-insensitive).</summary>
    public IReadOnlySet<string>? Genres { get; init; }

    public IReadOnlySet<string>? Series { get; init; }

    /// <summary>Acquired on (or, when that's unknown, added to the library).</summary>
    public DateFilter? Acquired { get; init; }

    /// <summary>Most recent finished read.</summary>
    public DateFilter? LastFinished { get; init; }

    public static LibraryFilter None { get; } = new();

    /// <summary>How many filters are set, not counting the search text (for the "Filters (n)" badge).</summary>
    public int ActiveCount =>
        (Statuses is { Count: > 0 } ? 1 : 0)
        + (Formats is { Count: > 0 } ? 1 : 0)
        + (ReadState != ReadState.Any ? 1 : 0)
        + (MinRating is not null ? 1 : 0)
        + (MinReadCount is not null ? 1 : 0)
        + (ShelfId is not null ? 1 : 0)
        + (BookcaseId is not null ? 1 : 0)
        + (Authors is { Count: > 0 } ? 1 : 0)
        + (Genres is { Count: > 0 } ? 1 : 0)
        + (Series is { Count: > 0 } ? 1 : 0)
        + (Acquired?.IsActive == true ? 1 : 0)
        + (LastFinished?.IsActive == true ? 1 : 0);
}

public sealed record LibrarySort(LibrarySortField Field = LibrarySortField.Author, bool Descending = false)
{
    public static LibrarySort Default { get; } = new();
}

/// <summary>Filter + sort + grouping.</summary>
public sealed record LibraryQuery
{
    public LibraryFilter Filter { get; init; } = LibraryFilter.None;

    public LibrarySort Sort { get; init; } = LibrarySort.Default;

    public LibraryGroupBy GroupBy { get; init; }
}

/// <summary>
/// The library's current view: the query plus how books are drawn. Remembered per device (<c>LibraryViewJson</c>).
/// The search text is deliberately not saved (see <c>LibraryViewJson</c>).
/// </summary>
public sealed record LibraryView
{
    public LibraryQuery Query { get; init; } = new();

    public LibraryDisplay Display { get; init; }

    /// <summary>Covers or list, for the Books display.</summary>
    public BooksLayout BooksLayout { get; init; }

    public static LibraryView Default { get; } = new();

    public LibraryFilter Filter => Query.Filter;

    public LibraryView WithFilter(LibraryFilter filter) => this with { Query = Query with { Filter = filter } };

    public LibraryView WithSort(LibrarySort sort) => this with { Query = Query with { Sort = sort } };

    public LibraryView WithGroupBy(LibraryGroupBy groupBy) => this with { Query = Query with { GroupBy = groupBy } };

    /// <summary>Same display, sort and grouping, with no filters (keeps the search text).</summary>
    public LibraryView ClearFilters() => WithFilter(new LibraryFilter { Search = Filter.Search });
}
