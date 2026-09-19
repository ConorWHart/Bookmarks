using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;
using static Bookmarks.Tests.TestData;

namespace Bookmarks.Tests;

public class LibraryQueryEngineTests
{
    [Fact]
    public void Filters_by_status()
    {
        var entries = new[]
        {
            Entry("Owned One"),
            Entry("Wished For", status: OwnershipStatus.Wishlist),
        };

        var result = LibraryQueryEngine.Apply(
            entries,
            new LibraryFilter { Statuses = new HashSet<OwnershipStatus> { OwnershipStatus.Wishlist } },
            LibrarySort.Default);

        Assert.Equal("Wished For", Assert.Single(result).Title);
    }

    [Theory]
    [InlineData(ReadState.Read, "Read Book")]
    [InlineData(ReadState.Unread, "Unread Book")]
    public void Filters_by_read_state(ReadState state, string expected)
    {
        var entries = new[] { Entry("Read Book", reads: 2), Entry("Unread Book") };

        var result = LibraryQueryEngine.Apply(entries, new LibraryFilter { ReadState = state }, LibrarySort.Default);

        Assert.Equal(expected, Assert.Single(result).Title);
    }

    [Fact]
    public void Search_matches_title_author_and_series_case_insensitively()
    {
        var entries = new[]
        {
            Entry("Dune", author: "Frank Herbert"),
            Entry("Foundation", author: "Isaac Asimov", series: "Foundation"),
            Entry("The Hobbit", author: "J.R.R. Tolkien", series: "Middle-earth"),
        };

        Assert.Single(LibraryQueryEngine.Apply(entries, new LibraryFilter { Search = "herbert" }, LibrarySort.Default));
        Assert.Single(LibraryQueryEngine.Apply(entries, new LibraryFilter { Search = "MIDDLE" }, LibrarySort.Default));
        Assert.Empty(LibraryQueryEngine.Apply(entries, new LibraryFilter { Search = "zzz" }, LibrarySort.Default));
    }

    [Fact]
    public void Title_sort_ignores_leading_articles()
    {
        var entries = new[] { Entry("The Zebra"), Entry("Apple"), Entry("A Mango") };

        var titles = LibraryQueryEngine.Apply(entries, LibraryFilter.None, new LibrarySort(LibrarySortField.Title))
            .Select(e => e.Title);

        Assert.Equal(new[] { "Apple", "A Mango", "The Zebra" }, titles);
    }

    [Fact]
    public void Author_sort_uses_surname()
    {
        var entries = new[]
        {
            Entry("B", author: "Zadie Smith"),
            Entry("A", author: "Isaac Asimov"),
            Entry("C", author: "Frank Herbert"),
        };

        var authors = LibraryQueryEngine.Apply(entries, LibraryFilter.None, new LibrarySort(LibrarySortField.Author))
            .Select(e => e.Author);

        Assert.Equal(new[] { "Isaac Asimov", "Frank Herbert", "Zadie Smith" }, authors);
    }

    [Fact]
    public void Series_sort_orders_by_series_number()
    {
        var entries = new[]
        {
            Entry("Third", series: "Saga", seriesNumber: 3),
            Entry("First", series: "Saga", seriesNumber: 1),
            Entry("Second", series: "Saga", seriesNumber: 2),
        };

        var titles = LibraryQueryEngine.Apply(entries, LibraryFilter.None, new LibrarySort(LibrarySortField.Series))
            .Select(e => e.Title);

        Assert.Equal(new[] { "First", "Second", "Third" }, titles);
    }

    [Fact]
    public void Color_sort_produces_rainbow_with_greys_last()
    {
        var entries = new[]
        {
            Entry("Black", color: "#111111"),
            Entry("Blue", color: "#1F4FBF"),
            Entry("Red", color: "#C0392B"),
            Entry("Green", color: "#2E8B57"),
            Entry("White", color: "#F8F8F8"),
        };

        var titles = LibraryQueryEngine.Apply(entries, LibraryFilter.None, new LibrarySort(LibrarySortField.Color))
            .Select(e => e.Title);

        Assert.Equal(new[] { "Red", "Green", "Blue", "White", "Black" }, titles);
    }

    [Fact]
    public void Shelf_order_sort_uses_rank_keys()
    {
        var entries = new[]
        {
            Entry("Second", shelfRank: "V"),
            Entry("First", shelfRank: "G"),
            Entry("Third", shelfRank: "k"),
        };

        var titles = LibraryQueryEngine.Apply(entries, LibraryFilter.None, new LibrarySort(LibrarySortField.ShelfOrder))
            .Select(e => e.Title);

        Assert.Equal(new[] { "First", "Second", "Third" }, titles);
    }

    [Fact]
    public void Grouping_by_genre_puts_books_under_each_genre()
    {
        var entries = new[]
        {
            Entry("Dune", genre: "Science Fiction"),
            Entry("The Hobbit", genre: "Fantasy"),
            Entry("Untagged"),
        };

        var groups = LibraryQueryEngine.Execute(entries, new LibraryQuery { GroupBy = LibraryGroupBy.Genre });

        Assert.Equal(new[] { "Fantasy", "Science Fiction", "Uncategorized" }, groups.Select(g => g.Key));
    }

    [Fact]
    public void Read_count_is_derived_from_finished_sessions()
        => Assert.Equal(3, Entry("Thrice", reads: 3).ReadCount);
}
