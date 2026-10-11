using Bookmarks.Core.Library;
using Bookmarks.Core.Lookup;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests.Lookup;

public class CoverBackfillTests
{
    private const string Cover = "https://covers.openlibrary.org/b/id/1-M.jpg";

    [Fact]
    public void Plan_sorts_books_into_lookups_color_only_and_skipped()
    {
        var needsLookup = Entry("A", isbn: "9780547928227");                                   // no cover
        var complete = Entry("B", isbn: "9780765326355", cover: Cover, color: "#112233");      // nothing to do
        var colorOnly = Entry("C", cover: Cover);                                             // cover, no color, details complete
        var noIsbn = Entry("D");                                                               // can't look up
        var badIsbn = Entry("E", isbn: "9780547928228");                                       // bad check digit
        var noIsbnButCover = Entry("F", cover: Cover, pages: null);                            // can't look up, can get a color

        var plan = CoverBackfill.Plan(new[] { needsLookup, complete, colorOnly, noIsbn, badIsbn, noIsbnButCover });

        Assert.Equal(new[] { "A" }, plan.Lookups.Select(e => e.Title).ToArray());
        Assert.Equal(new[] { "C", "F" }, plan.ColorOnly.Select(e => e.Title).ToArray());
        Assert.Equal(3, plan.WithoutIsbn);
        Assert.Equal(3, plan.Total);
    }

    [Fact]
    public void Missing_pages_or_authors_count_as_missing_details()
    {
        Assert.True(CoverBackfill.NeedsDetails(Entry("A", cover: Cover, color: "#112233", pages: null)));
        Assert.True(CoverBackfill.NeedsDetails(Entry("A", cover: Cover, color: "#112233", author: null)));
        Assert.False(CoverBackfill.NeedsDetails(Entry("A", cover: Cover, color: "#112233")));
    }

    [Fact]
    public void Shared_book_records_are_planned_once()
    {
        var first = Entry("A", isbn: "9780547928227");
        var second = LibraryEntry.Create(new LibraryItem { BookId = first.Book.Id }, first.Book, Array.Empty<ReadingSession>());

        Assert.Single(CoverBackfill.Plan(new[] { first, second }).Lookups);
    }

    [Fact]
    public void Use_cover_color_clears_a_stored_spine_color()
    {
        var book = new Book { Title = "X", CoverUrl = Cover };
        var item = new LibraryItem { BookId = book.Id, Appearance = new SpineAppearance { SpineColor = "#8C2F1B" } };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));
        model.UseCoverColor("#A1B2C3");
        Assert.Equal("#8C2F1B", model.SpineColor); // chosen color kept by default

        Assert.True(CoverBackfill.UseCoverColorForSpine(model));
        model.ApplyTo(book, item);

        Assert.Equal("#A1B2C3", book.DominantColor);
        Assert.Null(item.Appearance.SpineColor);
    }

    [Fact]
    public void Use_cover_color_without_a_color_does_nothing()
    {
        var model = new BookEditModel { Title = "X", SpineColor = "#8C2F1B" };
        Assert.False(CoverBackfill.UseCoverColorForSpine(model));
        Assert.Equal("#8C2F1B", model.SpineColor);
    }

    [Theory]
    [InlineData(1, "about 5 seconds")]
    [InlineData(20, "about 40 seconds")]
    [InlineData(40, "about a minute")]
    [InlineData(150, "about 5 minutes")]
    public void Duration_is_described(int lookups, string expected)
        => Assert.Equal(expected, CoverBackfill.DescribeDuration(lookups));

    private static LibraryEntry Entry(string title, string? isbn = null, string? cover = null, string? color = null, int? pages = 300, string? author = "Someone")
    {
        var book = new Book
        {
            Title = title,
            Isbn13 = isbn,
            CoverUrl = cover,
            DominantColor = color,
            PageCount = pages,
            Authors = author is null ? new List<string>() : new List<string> { author },
        };
        return LibraryEntry.Create(new LibraryItem { BookId = book.Id }, book, Array.Empty<ReadingSession>());
    }
}
