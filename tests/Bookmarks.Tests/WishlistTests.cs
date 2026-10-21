using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests;

public class WishlistTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static LibraryEntry Wish(string title, int? priority = null, int addedDay = 1, string? note = null, string author = "Test Author")
    {
        var entry = TestData.Entry(title, author, status: OwnershipStatus.Wishlist);
        entry.Item.WishlistPriority = priority;
        entry.Item.WishlistNote = note;
        entry.Item.AddedAt = T0.AddDays(addedDay);
        return entry;
    }

    [Fact]
    public void Only_wishlist_books_are_listed()
    {
        var owned = TestData.Entry("Owned book");
        var wish = Wish("Wanted");

        var result = Wishlist.Query([owned, wish], null, WishlistSort.Priority);

        Assert.Equal(new[] { "Wanted" }, result.Select(e => e.Title));
    }

    [Fact]
    public void Priority_sort_is_high_to_low_then_none_newest_first_within_each()
    {
        var entries = new[]
        {
            Wish("Low", Wishlist.Low, addedDay: 5),
            Wish("None old", null, addedDay: 1),
            Wish("High old", Wishlist.High, addedDay: 1),
            Wish("Medium", Wishlist.Medium, addedDay: 3),
            Wish("High new", Wishlist.High, addedDay: 9),
            Wish("None new", null, addedDay: 8),
        };

        var result = Wishlist.Query(entries, null, WishlistSort.Priority);

        Assert.Equal(new[] { "High new", "High old", "Medium", "Low", "None new", "None old" }, result.Select(e => e.Title));
    }

    [Fact]
    public void Other_sorts()
    {
        var entries = new[]
        {
            Wish("Beta", addedDay: 2, author: "Zed Adams"),
            Wish("Alpha", addedDay: 1, author: "Amy Young"),
            Wish("Gamma", addedDay: 3, author: "Max Brown"),
        };

        Assert.Equal(new[] { "Gamma", "Beta", "Alpha" }, Wishlist.Query(entries, null, WishlistSort.DateAdded).Select(e => e.Title));
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, Wishlist.Query(entries, null, WishlistSort.Title).Select(e => e.Title));
    }

    [Fact]
    public void Search_matches_book_details_and_the_note()
    {
        var entries = new[]
        {
            Wish("Dune", note: "Signed first edition at Powell's"),
            Wish("Emma", author: "Jane Austen"),
            Wish("Ulysses"),
        };

        Assert.Equal(new[] { "Dune" }, Wishlist.Query(entries, "powell", WishlistSort.Priority).Select(e => e.Title));
        Assert.Equal(new[] { "Emma" }, Wishlist.Query(entries, "austen", WishlistSort.Priority).Select(e => e.Title));
        Assert.Empty(Wishlist.Query(entries, "tolkien", WishlistSort.Priority));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(0, null)]
    [InlineData(4, null)]
    [InlineData(null, null)]
    public void Priorities_outside_1_to_3_are_none(int? input, int? expected)
        => Assert.Equal(expected, Wishlist.Normalize(input));

    [Fact]
    public void Notes_are_trimmed_capped_and_blank_is_none()
    {
        Assert.Null(Wishlist.NormalizeNote("   "));
        Assert.Equal("Powell's", Wishlist.NormalizeNote("  Powell's "));
        Assert.Equal(Wishlist.MaxNoteLength, Wishlist.NormalizeNote(new string('x', 600))!.Length);
    }

    [Fact]
    public void Editor_keeps_wishlist_fields_only_while_on_the_wishlist()
    {
        var entry = Wish("Dune", Wishlist.High, note: "Powell's");
        var model = BookEditModel.FromEntry(entry);
        Assert.Equal(Wishlist.High, model.WishlistPriority);
        Assert.Equal("Powell's", model.WishlistNote);

        model.WishlistNote = "  Waterstones  ";
        model.ApplyTo(entry.Book, entry.Item);
        Assert.Equal("Waterstones", entry.Item.WishlistNote);

        model.Status = OwnershipStatus.Owned;
        model.ApplyTo(entry.Book, entry.Item);
        Assert.Null(entry.Item.WishlistPriority);
        Assert.Null(entry.Item.WishlistNote);
    }
}
