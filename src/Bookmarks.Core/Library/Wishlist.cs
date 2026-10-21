using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

/// <summary>How the wishlist page orders books.</summary>
public enum WishlistSort
{
    /// <summary>High → Medium → Low → no priority; newest first within each.</summary>
    Priority,
    DateAdded,
    Title,
    Author,
}

/// <summary>Wishlist rules (Phase 7c): priorities, the note, and the page's query.</summary>
public static class Wishlist
{
    public const int High = 1;
    public const int Medium = 2;
    public const int Low = 3;

    /// <summary>Longest wishlist note (where to buy, edition wanted, price seen).</summary>
    public const int MaxNoteLength = 500;

    public static readonly IReadOnlyList<(int? Priority, string Label)> Priorities =
    [
        (High, "High"),
        (Medium, "Medium"),
        (Low, "Low"),
        (null, "No priority"),
    ];

    public static string PriorityLabel(int? priority) => priority switch
    {
        High => "High",
        Medium => "Medium",
        Low => "Low",
        _ => "No priority",
    };

    /// <summary>A stored priority: 1–3, anything else is none.</summary>
    public static int? Normalize(int? priority) => priority is >= High and <= Low ? priority : null;

    /// <summary>Trimmed, at most <see cref="MaxNoteLength"/> characters; blank is null.</summary>
    public static string? NormalizeNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxNoteLength ? trimmed[..MaxNoteLength].TrimEnd() : trimmed;
    }

    /// <summary>The wishlist books matching <paramref name="search"/> (title, author, series, ISBN or note), in order.</summary>
    public static IReadOnlyList<LibraryEntry> Query(IEnumerable<LibraryEntry> entries, string? search, WishlistSort sort)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var wished = entries.Where(e => e.Item.Status == OwnershipStatus.Wishlist).ToList();
        var q = search?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            // The library's own search, plus the wishlist note ("Powell's", "first edition").
            var matched = LibraryQueryEngine
                .Apply(wished, LibraryFilter.None with { Search = q }, LibrarySort.Default)
                .Select(e => e.Id)
                .ToHashSet();
            wished = wished
                .Where(e => matched.Contains(e.Id)
                    || (e.Item.WishlistNote?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return sort switch
        {
            WishlistSort.Title => LibraryQueryEngine.Apply(wished, LibraryFilter.None, new LibrarySort(LibrarySortField.Title)),
            WishlistSort.Author => LibraryQueryEngine.Apply(wished, LibraryFilter.None, new LibrarySort(LibrarySortField.Author)),
            WishlistSort.DateAdded => NewestFirst(wished).ToList(),
            _ => NewestFirst(wished).OrderBy(e => Normalize(e.Item.WishlistPriority) ?? int.MaxValue).ToList(),
        };
    }

    private static IOrderedEnumerable<LibraryEntry> NewestFirst(IEnumerable<LibraryEntry> entries)
        => entries.OrderByDescending(e => e.Item.AddedAt).ThenBy(e => e.Id);
}
