using Bookmarks.Core.Ranking;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Domain;

namespace Bookmarks.UI.Services;

/// <summary>Demo library so the shelf view has something to render during early development.</summary>
internal static class SampleData
{
    private static readonly (string Title, string Author, int Pages, string Color, BookFormat Format, string? Series, int? No, string Genre, int Reads)[] Books =
    [
        ("The Hobbit", "J.R.R. Tolkien", 310, "#2E5E4E", BookFormat.Hardcover, "Middle-earth", 0, "Fantasy", 3),
        ("The Fellowship of the Ring", "J.R.R. Tolkien", 423, "#8C2F1B", BookFormat.Hardcover, "The Lord of the Rings", 1, "Fantasy", 2),
        ("The Two Towers", "J.R.R. Tolkien", 352, "#1F3A5F", BookFormat.Hardcover, "The Lord of the Rings", 2, "Fantasy", 2),
        ("The Return of the King", "J.R.R. Tolkien", 416, "#C9A227", BookFormat.Hardcover, "The Lord of the Rings", 3, "Fantasy", 1),
        ("A Wizard of Earthsea", "Ursula K. Le Guin", 183, "#3B7EA1", BookFormat.Paperback, "Earthsea", 1, "Fantasy", 1),
        ("The Left Hand of Darkness", "Ursula K. Le Guin", 304, "#E8E4D8", BookFormat.Paperback, null, null, "Science Fiction", 1),
        ("Dune", "Frank Herbert", 688, "#D9822B", BookFormat.MassMarketPaperback, "Dune", 1, "Science Fiction", 2),
        ("Foundation", "Isaac Asimov", 255, "#5B3F8C", BookFormat.MassMarketPaperback, "Foundation", 1, "Science Fiction", 0),
        ("Neuromancer", "William Gibson", 271, "#111111", BookFormat.Paperback, "Sprawl", 1, "Science Fiction", 1),
        ("The Name of the Wind", "Patrick Rothfuss", 662, "#6E2C2C", BookFormat.Hardcover, "The Kingkiller Chronicle", 1, "Fantasy", 1),
        ("Piranesi", "Susanna Clarke", 272, "#7FB7BE", BookFormat.Hardcover, null, null, "Fantasy", 1),
        ("Project Hail Mary", "Andy Weir", 476, "#F2C14E", BookFormat.Hardcover, null, null, "Science Fiction", 1),
        ("The Martian", "Andy Weir", 369, "#B5452F", BookFormat.Paperback, null, null, "Science Fiction", 0),
        ("Circe", "Madeline Miller", 393, "#A87C2A", BookFormat.Paperback, null, null, "Mythology", 1),
        ("Pride and Prejudice", "Jane Austen", 432, "#F4D6DA", BookFormat.LeatherBound, null, null, "Classics", 2),
        ("Moby-Dick", "Herman Melville", 720, "#274060", BookFormat.LeatherBound, null, null, "Classics", 0),
        ("Frankenstein", "Mary Shelley", 280, "#4A5D23", BookFormat.Paperback, null, null, "Classics", 1),
        ("The Road", "Cormac McCarthy", 287, "#8E8E8E", BookFormat.Paperback, null, null, "Literary Fiction", 1),
        ("Educated", "Tara Westover", 352, "#E07A5F", BookFormat.Paperback, null, null, "Memoir", 0),
        ("Sapiens", "Yuval Noah Harari", 443, "#C1121F", BookFormat.Hardcover, null, null, "History", 1),
    ];

    public static async Task SeedAsync(ILocalStore store, CancellationToken ct)
    {
        var userId = Guid.Empty; // replaced by the signed-in user's id in Phase 3

        var bookcase = new Bookcase { UserId = userId, Name = "Living room", Theme = "oak", SortRank = RankKey.Between(null, null) };
        await store.UpsertAsync(bookcase, ct);

        var shelfRanks = RankKey.Sequence(3);
        var shelves = new List<Shelf>();
        for (var i = 0; i < shelfRanks.Count; i++)
        {
            var shelf = new Shelf { UserId = userId, BookcaseId = bookcase.Id, Name = $"Shelf {i + 1}", SortRank = shelfRanks[i], WidthMm = 800 };
            shelves.Add(shelf);
            await store.UpsertAsync(shelf, ct);
        }

        var itemRanks = RankKey.Sequence(Books.Length);
        for (var i = 0; i < Books.Length; i++)
        {
            var s = Books[i];
            var book = new Book
            {
                Title = s.Title,
                Authors = [s.Author],
                PageCount = s.Pages,
                DominantColor = s.Color,
                Series = s.Series,
                SeriesNumber = s.No,
                Genres = [s.Genre],
                Source = BookSource.Manual,
                CreatedByUserId = userId,
            };
            await store.UpsertBookAsync(book, ct);

            var item = new LibraryItem
            {
                UserId = userId,
                BookId = book.Id,
                Format = s.Format,
                Status = i % 9 == 8 ? OwnershipStatus.Wishlist : OwnershipStatus.Owned,
                ShelfId = shelves[i % shelves.Count].Id,
                ShelfRank = itemRanks[i],
                WantToReread = s.Reads > 1,
                Rating = s.Reads > 0 ? 3.5m + (i % 4 * 0.5m) : null,
                AddedAt = DateTimeOffset.UtcNow.AddDays(-i * 11),
                Appearance = new SpineAppearance { FaceOut = s.Title is "Piranesi" or "Circe" },
            };
            await store.UpsertAsync(item, ct);

            for (var r = 0; r < s.Reads; r++)
            {
                var finished = DateOnly.FromDateTime(DateTime.Today).AddYears(-(r + 1)).AddDays(-i);
                await store.UpsertAsync(
                    new ReadingSession
                    {
                        UserId = userId,
                        LibraryItemId = item.Id,
                        StartedOn = finished.AddDays(-21),
                        FinishedOn = finished,
                        Outcome = ReadingOutcome.Finished,
                    },
                    ct);
            }
        }
    }
}
