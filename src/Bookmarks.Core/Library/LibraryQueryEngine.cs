using Bookmarks.Core.Ranking;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

public sealed record LibraryGroup(string Key, IReadOnlyList<LibraryEntry> Entries);

/// <summary>
/// Pure, in-memory filter / sort / group over <see cref="LibraryEntry"/>.
/// Runs entirely on the device, so the library view behaves identically offline.
/// </summary>
public static class LibraryQueryEngine
{
    private static readonly string[] Articles = ["The ", "A ", "An "];

    public static IReadOnlyList<LibraryEntry> Apply(IEnumerable<LibraryEntry> entries, LibraryFilter filter, LibrarySort sort)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(sort);

        var today = DateOnly.FromDateTime(DateTime.Now);
        return Sort(entries.Where(e => Matches(e, filter, today)), sort).ToList();
    }

    public static IReadOnlyList<LibraryGroup> Execute(IEnumerable<LibraryEntry> entries, LibraryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sorted = Apply(entries, query.Filter, query.Sort);

        if (query.GroupBy == LibraryGroupBy.None)
        {
            return [new LibraryGroup("", sorted)];
        }

        // Group keys are ordered alphabetically; entries keep the sort order within each group.
        // A book with several genres appears in each genre group.
        return sorted
            .SelectMany(e => GroupKeys(e, query.GroupBy).Select(k => (Key: k, Entry: e)))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LibraryGroup(g.Key, g.Select(x => x.Entry).ToList()))
            .ToList();
    }

    public static bool Matches(LibraryEntry e, LibraryFilter f)
        => Matches(e, f, DateOnly.FromDateTime(DateTime.Now));

    /// <param name="today">Reference date for relative date filters ("last 30 days").</param>
    public static bool Matches(LibraryEntry e, LibraryFilter f, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(f);

        if (f.Statuses is { Count: > 0 } && !f.Statuses.Contains(e.Item.Status))
        {
            return false;
        }

        if (f.Formats is { Count: > 0 } && !f.Formats.Contains(e.Item.Format))
        {
            return false;
        }


        if (f.WantToReread is { } reread && e.Item.WantToReread != reread)
        {
            return false;
        }

        if (f.MinRating is { } min && (e.Item.Rating ?? 0) < min)
        {
            return false;
        }

        if (f.ShelfId is { } shelf && !e.ShelfIds.Contains(shelf))
        {
            return false;
        }

        if (f.BookcaseId is { } bookcase)
        {
            var placed = bookcase == LibraryFilter.Unshelved ? e.BookcaseIds.Count == 0 : e.BookcaseIds.Contains(bookcase);
            if (!placed)
            {
                return false;
            }
        }

        if (f.MinReadCount is { } minReads && e.ReadCount < minReads)
        {
            return false;
        }

        if (f.Authors is { Count: > 0 } authors && !e.Book.Authors.Any(a => authors.Contains(a, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (f.Genres is { Count: > 0 } genres && !e.Book.Genres.Any(g => genres.Contains(g, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (f.Series is { Count: > 0 } series && (e.Book.Series is not { } s || !series.Contains(s, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (f.Acquired is { } acquired
            && !acquired.Matches(e.Item.AcquiredOn ?? DateOnly.FromDateTime(e.Item.AddedAt.LocalDateTime), today))
        {
            return false;
        }

        if (f.LastFinished is { } finished && !finished.Matches(e.LastFinishedOn, today))
        {
            return false;
        }

        var readOk = f.ReadState switch
        {
            ReadState.Read => e.IsRead,
            ReadState.Unread => !e.IsRead,
            ReadState.Reading => e.IsReading,
            ReadState.ToRead => !e.IsRead && !e.IsReading && e.Item.Status != OwnershipStatus.Wishlist,
            _ => true,
        };
        if (!readOk)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var q = f.Search.Trim();
            var b = e.Book;
            var hit = b.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (b.Subtitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (b.Series?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || b.Authors.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (b.Isbn13?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!hit)
            {
                return false;
            }
        }

        return true;
    }

    private static IOrderedEnumerable<LibraryEntry> Sort(IEnumerable<LibraryEntry> source, LibrarySort sort)
    {
        var ordered = sort.Field switch
        {
            LibrarySortField.Title => By(source, e => SortableTitle(e.Title), StringComparer.OrdinalIgnoreCase, sort.Descending),
            LibrarySortField.Author => By(source, e => AuthorSortKey(e.Author), StringComparer.OrdinalIgnoreCase, sort.Descending),
            LibrarySortField.Series => By(source, e => e.Book.Series ?? "\uffff", StringComparer.OrdinalIgnoreCase, sort.Descending)
                .ThenBy(e => e.Book.SeriesNumber ?? decimal.MaxValue),
            LibrarySortField.DateAdded => By(source, e => e.Item.AddedAt, Comparer<DateTimeOffset>.Default, sort.Descending),
            LibrarySortField.Rating => By(source, e => e.Item.Rating ?? -1, Comparer<decimal>.Default, sort.Descending),
            LibrarySortField.Height => By(source, e => SpineMetrics.HeightMm(e), Comparer<int>.Default, sort.Descending),
            LibrarySortField.Color => By(source, e => ColorUtil.RainbowKey(e.SpineColor), Comparer<double>.Default, sort.Descending),
            LibrarySortField.ReadCount => By(source, e => e.ReadCount, Comparer<int>.Default, sort.Descending),
            _ => By(source, e => e.Item.ShelfRank ?? "\uffff", Comparer<string>.Create(RankKey.Compare), sort.Descending),
        };

        // Stable, deterministic tiebreakers.
        return ordered
            .ThenBy(e => AuthorSortKey(e.Author), StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Book.Series ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Book.SeriesNumber ?? 0)
            .ThenBy(e => SortableTitle(e.Title), StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id);
    }

    private static IOrderedEnumerable<LibraryEntry> By<TKey>(
        IEnumerable<LibraryEntry> source, Func<LibraryEntry, TKey> key, IComparer<TKey> comparer, bool descending)
        => descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);

    private static IEnumerable<string> GroupKeys(LibraryEntry e, LibraryGroupBy groupBy) => groupBy switch
    {
        LibraryGroupBy.Author => [e.Author.Length > 0 ? e.Author : "Unknown author"],
        LibraryGroupBy.Genre => e.Book.Genres.Count > 0 ? e.Book.Genres : ["Uncategorized"],
        LibraryGroupBy.Status => [e.Item.Status.ToString()],
        LibraryGroupBy.Series => [e.Book.Series ?? "Standalone"],
        LibraryGroupBy.Format => [e.Item.Format.ToString()],
        _ => [""],
    };

    /// <summary>"The Hobbit" sorts under H.</summary>
    internal static string SortableTitle(string title)
    {
        foreach (var article in Articles)
        {
            if (title.StartsWith(article, StringComparison.OrdinalIgnoreCase) && title.Length > article.Length)
            {
                return title[article.Length..];
            }
        }

        return title;
    }

    /// <summary>"Ursula K. Le Guin" → "Le Guin, Ursula K." is out of scope; sort by last token for now.</summary>
    internal static string AuthorSortKey(string author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return "\uffff";
        }

        var parts = author.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0] : $"{parts[^1]} {string.Join(' ', parts[..^1])}";
    }
}
