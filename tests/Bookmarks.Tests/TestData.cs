using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests;

internal static class TestData
{
    public static LibraryEntry Entry(
        string title,
        string author = "Test Author",
        string? color = null,
        int reads = 0,
        OwnershipStatus status = OwnershipStatus.Owned,
        BookFormat format = BookFormat.Paperback,
        int? pages = 300,
        string? series = null,
        decimal? seriesNumber = null,
        string? genre = null,
        decimal? rating = null,
        string? shelfRank = null)
    {
        var book = new Book
        {
            Title = title,
            Authors = [author],
            DominantColor = color,
            PageCount = pages,
            Series = series,
            SeriesNumber = seriesNumber,
            Genres = genre is null ? new List<string>() : new List<string> { genre },
        };
        var item = new LibraryItem
        {
            BookId = book.Id,
            Status = status,
            Format = format,
            Rating = rating,
            ShelfRank = shelfRank,
        };
        var sessions = Enumerable.Range(0, reads).Select(_ => new ReadingSession
        {
            LibraryItemId = item.Id,
            Outcome = ReadingOutcome.Finished,
            FinishedOn = new DateOnly(2024, 1, 1),
        });

        return LibraryEntry.Create(item, book, sessions);
    }
}
