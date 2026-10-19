using Bookmarks.Core.Library;
using static Bookmarks.Tests.TestData;

namespace Bookmarks.Tests;

public class StackLayoutEngineTests
{
    // 200 pages of standard paper as a paperback: 200 × 0.065 + 1 = 14 mm thick, 210 mm tall.
    private static LibraryEntry Book(string title) => Entry(title, pages: 200);

    [Fact]
    public void Books_keep_their_order_and_a_pile_ends_before_it_gets_too_tall()
    {
        var books = new[] { Book("A"), Book("B"), Book("C"), Book("D"), Book("E") };

        var piles = StackLayoutEngine.Piles(books, maxHeightMm: 30, maxBooks: 10);

        Assert.Equal(new[] { 2, 2, 1 }, piles.Select(p => p.Books.Count));
        Assert.Equal(new[] { "A", "B", "C", "D", "E" }, piles.SelectMany(p => p.Books).Select(b => b.Entry.Title));
        Assert.Equal(28, piles[0].HeightMm);
        Assert.Equal(210, piles[0].WidthMm);
    }

    [Fact]
    public void A_pile_also_ends_at_the_book_limit_and_a_huge_book_still_gets_a_pile()
    {
        var piles = StackLayoutEngine.Piles(new[] { Book("A"), Book("B"), Book("C") }, maxHeightMm: 1000, maxBooks: 2);
        Assert.Equal(new[] { 2, 1 }, piles.Select(p => p.Books.Count));

        var thick = Entry("Thick", pages: 1200);
        Assert.Single(StackLayoutEngine.Piles(new[] { thick }, maxHeightMm: 10, maxBooks: 5));
    }

    [Fact]
    public void Thin_books_are_drawn_at_the_minimum_thickness()
    {
        var pamphlet = Entry("Pamphlet", pages: 20);

        var pile = StackLayoutEngine.Piles(new[] { pamphlet }, maxHeightMm: 500, maxBooks: 5, minThicknessMm: 15).Single();

        Assert.Equal(15, pile.Books[0].WidthMm);
    }

    [Fact]
    public void Piles_wrap_into_rows_by_width()
    {
        var piles = StackLayoutEngine.Piles(new[] { Book("A"), Book("B"), Book("C") }, maxHeightMm: 20, maxBooks: 1);

        // 210 + 20 + 210 = 440 fits in 450; a third pile doesn't.
        var rows = StackLayoutEngine.Rows(piles, rowWidthMm: 450, gapMm: 20);

        Assert.Equal(new[] { 2, 1 }, rows.Select(r => r.Count));
        Assert.Single(StackLayoutEngine.Rows(piles.Take(1).ToList(), rowWidthMm: 100, gapMm: 20));
    }
}
