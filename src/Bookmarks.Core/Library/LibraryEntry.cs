using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

/// <summary>
/// Read model for the library view: a copy joined with its catalog data and derived reading stats.
/// Built once from the local store and kept in memory; all filtering/sorting runs over these.
/// </summary>
public sealed record LibraryEntry(
    LibraryItem Item,
    Book Book,
    int ReadCount,
    bool IsReading,
    DateOnly? LastFinishedOn)
{
    public Guid Id => Item.Id;

    public string Title => Book.Title;

    public string Author => Book.PrimaryAuthor;

    public bool IsRead => ReadCount > 0;

    /// <summary>Copy override first, then catalog color, then a neutral fallback.</summary>
    public string SpineColor => Item.Appearance.SpineColor ?? Book.DominantColor ?? "#6B5B4B";

    public static LibraryEntry Create(LibraryItem item, Book book, IEnumerable<ReadingSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(sessions);

        var mine = sessions.Where(s => s.LibraryItemId == item.Id && !s.IsDeleted).ToList();
        var finished = mine.Where(s => s.Outcome == ReadingOutcome.Finished).ToList();

        return new LibraryEntry(
            item,
            book,
            ReadCount: finished.Count,
            IsReading: mine.Any(s => s.Outcome == ReadingOutcome.InProgress),
            LastFinishedOn: finished.Max(s => s.FinishedOn));
    }
}
