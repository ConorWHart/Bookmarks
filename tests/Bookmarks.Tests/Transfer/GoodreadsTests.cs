using Bookmarks.Core.Storage;
using Bookmarks.Core.Transfer;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests.Transfer;

public class GoodreadsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.CreateVersion7();

    // A trimmed Goodreads export (real column order; values as Goodreads writes them).
    private const string Header =
        "Book Id,Title,Author,Author l-f,Additional Authors,ISBN,ISBN13,My Rating,Average Rating,Publisher,Binding,"
        + "Number of Pages,Year Published,Original Publication Year,Date Read,Date Added,Bookshelves,"
        + "Bookshelves with positions,Exclusive Shelf,My Review,Spoiler,Private Notes,Read Count,Owned Copies";

    private static readonly string Export = string.Join("\n",
        Header,
        "234225,\"Dune (Dune, #1)\",Frank Herbert,\"Herbert, Frank\",,\"=\"\"0441172717\"\"\",\"=\"\"9780441172719\"\"\",5,4.25,Ace,Mass Market Paperback,"
            + "896,1990,1965,2021/03/15,2019/01/02,read,read (#3),read,\"Loved it.<br/><br/>Again &amp; again.\",,Mum's copy,2,1",
        "3,\"Harry Potter and the Sorcerer's Stone (Harry Potter, #1)\",J.K.  Rowling,\"Rowling, J.K.\",Mary GrandPré,\"=\"\"\"\"\",\"=\"\"\"\"\",0,4.47,Scholastic,Hardcover,"
            + "309,1998,1997,,2020/05/05,currently-reading,currently-reading (#1),currently-reading,,,,0,0",
        "11,The Hitchhiker's Guide to the Galaxy,Douglas Adams,\"Adams, Douglas\",,\"=\"\"\"\"\",\"=\"\"\"\"\",0,4.22,Del Rey,Paperback,"
            + "193,1995,1979,,2021/06/01,to-read,to-read (#2),to-read,,,,0,0",
        "12,Owned Unread,Some Author,,,,,0,,,Hardcover,,,,,2021/06/02,to-read,,to-read,,,,0,1",
        ",,No Title Here,,,,,0,,,,,,,,,,,read,,,,1,0");

    [Fact]
    public void Parses_a_goodreads_export()
    {
        var rows = GoodreadsCsv.Parse(Export);

        Assert.Equal(4, rows.Count); // the row without a title is skipped

        var dune = rows[0];
        Assert.Equal("Dune", dune.Title);
        Assert.Equal("Dune", dune.Series);
        Assert.Equal(1m, dune.SeriesNumber);
        Assert.Equal(new[] { "Frank Herbert" }, dune.Authors);
        Assert.Equal("9780441172719", dune.Isbn13);
        Assert.Equal(5, dune.Rating);
        Assert.Equal(BookFormat.MassMarketPaperback, dune.Format);
        Assert.Equal(896, dune.PageCount);
        Assert.Equal(1990, dune.PublishedYear);
        Assert.Equal(new DateOnly(2021, 3, 15), dune.DateRead);
        Assert.Equal(new DateOnly(2019, 1, 2), dune.DateAdded);
        Assert.Equal(GoodreadsBook.Read, dune.ExclusiveShelf);
        Assert.Equal(2, dune.ReadCount);
        Assert.Equal(1, dune.OwnedCopies);
        Assert.Equal("Loved it.\n\nAgain & again.", dune.Review);
        Assert.Equal("Mum's copy", dune.PrivateNotes);

        var potter = rows[1];
        Assert.Equal("Harry Potter and the Sorcerer's Stone", potter.Title);
        Assert.Equal(new[] { "J.K. Rowling", "Mary GrandPré" }, potter.Authors);
        Assert.Null(potter.Isbn13);
        Assert.Null(potter.Rating);
        Assert.Equal(BookFormat.Hardcover, potter.Format);
    }

    [Theory]
    [InlineData("Dune (Dune, #1)", "Dune", "Dune", "1")]
    [InlineData("The Way of Kings (The Stormlight Archive #1)", "The Way of Kings", "The Stormlight Archive", "1")]
    [InlineData("Edgedancer (The Stormlight Archive, #2.5)", "Edgedancer", "The Stormlight Archive", "2.5")]
    [InlineData("The Lord of the Rings (The Lord of the Rings, #1-3)", "The Lord of the Rings", "The Lord of the Rings", "1")]
    [InlineData("Dune (Penguin Classics)", "Dune (Penguin Classics)", null, null)]
    public void Splits_the_series_suffix(string input, string title, string? series, string? number)
    {
        var result = GoodreadsCsv.SplitSeries(input);

        Assert.Equal(title, result.Title);
        Assert.Equal(series, result.Series);
        Assert.Equal(number is null ? null : decimal.Parse(number, System.Globalization.CultureInfo.InvariantCulture), result.Number);
    }

    [Fact]
    public void A_file_that_isnt_a_goodreads_export_is_refused()
    {
        Assert.Throws<GoodreadsFormatException>(() => GoodreadsCsv.Parse("name,email\nConor,c@example.com"));
        Assert.Throws<GoodreadsFormatException>(() => GoodreadsCsv.Parse(""));
    }

    [Fact]
    public async Task Import_maps_shelves_reads_ratings_and_notes()
    {
        var store = new InMemoryLocalStore();
        var rows = GoodreadsCsv.Parse(Export);

        var plan = await GoodreadsImport.PlanAsync(store, rows, OwnershipStatus.Wishlist, Owner, Now);
        await GoodreadsImport.ApplyAsync(store, plan);

        Assert.Equal(4, plan.NewBooks);
        Assert.Equal(0, plan.ExistingUpdated);
        Assert.Equal(1, plan.Read);
        Assert.Equal(1, plan.CurrentlyReading);
        Assert.Equal(2, plan.ToRead);
        Assert.Equal(1, plan.Wishlist); // the to-read book with Owned Copies = 1 is Owned
        Assert.Equal(4, plan.NewItemIds.Count);

        var books = await store.GetBooksAsync();
        var items = await store.GetAllAsync<LibraryItem>();
        var sessions = await store.GetAllAsync<ReadingSession>();
        var notes = await store.GetAllAsync<Note>();
        LibraryItem ItemOf(string title) => items.Single(i => books.Single(b => b.Id == i.BookId).Title == title);

        var dune = ItemOf("Dune");
        Assert.Equal(OwnershipStatus.Owned, dune.Status);
        Assert.Equal(5m, dune.Rating);
        Assert.Equal(BookFormat.MassMarketPaperback, dune.Format);
        Assert.Equal(new DateTimeOffset(2019, 1, 2, 0, 0, 0, TimeSpan.Zero), dune.AddedAt);
        var duneReads = sessions.Where(s => s.LibraryItemId == dune.Id).ToList();
        Assert.Equal(2, duneReads.Count);
        Assert.All(duneReads, s => Assert.Equal(ReadingOutcome.Finished, s.Outcome));
        Assert.Contains(duneReads, s => s.FinishedOn == new DateOnly(2021, 3, 15));
        Assert.Equal(2, notes.Count(n => n.LibraryItemId == dune.Id));
        Assert.Contains(notes, n => n.Body == "Mum's copy");

        var potter = ItemOf("Harry Potter and the Sorcerer's Stone");
        Assert.Equal(ReadingOutcome.InProgress, Assert.Single(sessions, s => s.LibraryItemId == potter.Id).Outcome);

        Assert.Equal(OwnershipStatus.Wishlist, ItemOf("The Hitchhiker's Guide to the Galaxy").Status);
        Assert.Equal(OwnershipStatus.Owned, ItemOf("Owned Unread").Status);
        Assert.All(items, i => Assert.Equal(Owner, i.UserId));
        Assert.Equal(1, books.Count(b => b.Series == "Dune" && b.SeriesNumber == 1m));
    }

    [Fact]
    public async Task Importing_twice_adds_nothing_the_second_time()
    {
        var store = new InMemoryLocalStore();
        await GoodreadsImport.ApplyAsync(store, await GoodreadsImport.PlanAsync(store, GoodreadsCsv.Parse(Export), OwnershipStatus.Owned, Owner, Now));
        var counts = await CountsAsync(store);

        var again = await GoodreadsImport.PlanAsync(store, GoodreadsCsv.Parse(Export), OwnershipStatus.Owned, Owner, Now);
        await GoodreadsImport.ApplyAsync(store, again);

        Assert.Equal(0, again.NewBooks);
        Assert.Equal(0, again.ExistingUpdated);
        Assert.Equal(4, again.ExistingUnchanged);
        Assert.Equal(counts, await CountsAsync(store));
    }

    [Fact]
    public async Task A_book_already_in_the_library_only_gets_what_it_lacks()
    {
        var store = new InMemoryLocalStore();
        var book = new Book { UserId = Owner, Title = "Dune", Authors = ["Frank Herbert"], Isbn13 = "9780441172719" };
        var item = new LibraryItem { UserId = Owner, BookId = book.Id, Status = OwnershipStatus.Owned, Rating = 3m };
        await store.UpsertAsync(book);
        await store.UpsertAsync(item);

        var plan = await GoodreadsImport.PlanAsync(store, GoodreadsCsv.Parse(Export), OwnershipStatus.Wishlist, Owner, Now);
        await GoodreadsImport.ApplyAsync(store, plan);

        Assert.Equal(3, plan.NewBooks);
        Assert.Equal(1, plan.ExistingUpdated);
        Assert.Single(await store.GetBooksAsync(), b => b.Title == "Dune");
        Assert.Equal(3m, (await store.GetAsync<LibraryItem>(item.Id))!.Rating); // the user's own rating stays
        Assert.Equal(2, (await store.GetAllAsync<ReadingSession>()).Count(s => s.LibraryItemId == item.Id));
    }

    [Fact]
    public async Task Planning_does_not_change_the_library()
    {
        var store = new InMemoryLocalStore();
        var book = new Book { UserId = Owner, Title = "Dune", Authors = ["Frank Herbert"], Isbn13 = "9780441172719" };
        var item = new LibraryItem { UserId = Owner, BookId = book.Id };
        await store.UpsertAsync(book);
        await store.UpsertAsync(item);

        await GoodreadsImport.PlanAsync(store, GoodreadsCsv.Parse(Export), OwnershipStatus.Wishlist, Owner, Now);

        Assert.Null((await store.GetAsync<LibraryItem>(item.Id))!.Rating);
        Assert.Empty(await store.GetAllAsync<ReadingSession>());
    }

    [Fact]
    public async Task Export_writes_goodreads_columns_and_reimports_the_same_library()
    {
        var store = new InMemoryLocalStore();
        await GoodreadsImport.ApplyAsync(store, await GoodreadsImport.PlanAsync(store, GoodreadsCsv.Parse(Export), OwnershipStatus.Wishlist, Owner, Now));

        var csv = await GoodreadsCsv.ExportAsync(store);
        var records = Csv.Parse(csv);

        Assert.Equal(GoodreadsCsv.Columns, records[0]);
        var dune = records.Single(r => r[2] == "Frank Herbert");
        string Col(string[] row, string name) => row[GoodreadsCsv.Columns.ToList().IndexOf(name)];
        Assert.Equal("Dune (Dune, #1)", Col(dune, "Title"));
        Assert.Equal("Herbert, Frank", Col(dune, "Author l-f"));
        Assert.Equal("=\"0441172717\"", Col(dune, "ISBN"));
        Assert.Equal("=\"9780441172719\"", Col(dune, "ISBN13"));
        Assert.Equal("5", Col(dune, "My Rating"));
        Assert.Equal("Mass Market Paperback", Col(dune, "Binding"));
        Assert.Equal("2021/03/15", Col(dune, "Date Read"));
        Assert.Equal("2019/01/02", Col(dune, "Date Added"));
        Assert.Equal("read", Col(dune, "Exclusive Shelf"));
        Assert.Equal("2", Col(dune, "Read Count"));
        Assert.Equal("1", Col(dune, "Owned Copies"));
        Assert.Contains("Mum's copy", Col(dune, "Private Notes"), StringComparison.Ordinal);

        var wish = records.Single(r => r[2] == "Douglas Adams");
        Assert.Equal("to-read", Col(wish, "Exclusive Shelf"));
        Assert.Equal("to-read, wishlist", Col(wish, "Bookshelves"));
        Assert.Equal("0", Col(wish, "Owned Copies"));

        // Our own export imports into an empty library as the same books.
        var other = new InMemoryLocalStore();
        var plan = await GoodreadsImport.PlanAsync(other, GoodreadsCsv.Parse(csv), OwnershipStatus.Wishlist, Owner, Now);
        Assert.Equal(4, plan.NewBooks);
        Assert.Equal(1, plan.Wishlist);
    }

    private static async Task<(int, int, int, int)> CountsAsync(ILocalStore store)
        => ((await store.GetBooksAsync()).Count,
            (await store.GetAllAsync<LibraryItem>()).Count,
            (await store.GetAllAsync<ReadingSession>()).Count,
            (await store.GetAllAsync<Note>()).Count);
}
