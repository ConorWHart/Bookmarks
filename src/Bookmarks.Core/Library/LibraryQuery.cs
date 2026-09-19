using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

public enum ReadState
{
    Any = 0,
    Read = 1,
    Unread = 2,
    Reading = 3,
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

/// <summary>
/// Filter criteria. Null / empty means "don't filter on this". Serialized into filter presets,
/// so keep it a plain data record and add properties rather than renaming them.
/// </summary>
public sealed record LibraryFilter
{
    public string? Search { get; init; }

    public IReadOnlySet<OwnershipStatus>? Statuses { get; init; }

    public IReadOnlySet<BookFormat>? Formats { get; init; }

    public IReadOnlySet<Guid>? TagIds { get; init; }

    public ReadState ReadState { get; init; }

    public bool? WantToReread { get; init; }

    public decimal? MinRating { get; init; }

    public Guid? ShelfId { get; init; }

    public static LibraryFilter None { get; } = new();
}

public sealed record LibrarySort(LibrarySortField Field = LibrarySortField.Author, bool Descending = false)
{
    public static LibrarySort Default { get; } = new();
}

/// <summary>The full "view definition" that a filter preset stores.</summary>
public sealed record LibraryQuery
{
    public LibraryFilter Filter { get; init; } = LibraryFilter.None;

    public LibrarySort Sort { get; init; } = LibrarySort.Default;

    public LibraryGroupBy GroupBy { get; init; }
}
