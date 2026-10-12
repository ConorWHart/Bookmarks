using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests;

public class LibraryViewJsonTests
{
    private static LibraryView FullView() => new()
    {
        Arrangement = LibraryArrangement.BySort,
        Display = LibraryDisplay.Covers,
        Query = new LibraryQuery
        {
            Sort = new LibrarySort(LibrarySortField.Color, Descending: true),
            GroupBy = LibraryGroupBy.Genre,
            Filter = new LibraryFilter
            {
                Statuses = new HashSet<OwnershipStatus> { OwnershipStatus.Wishlist, OwnershipStatus.Owned },
                Formats = new HashSet<BookFormat> { BookFormat.Hardcover },
                TagIds = new HashSet<Guid> { Guid.Parse("0192aaaa-0000-7000-8000-000000000001") },
                TagMatchAll = true,
                ReadState = ReadState.Unread,
                WantToReread = true,
                MinRating = 3.5m,
                MinReadCount = 2,
                BookcaseId = LibraryFilter.Unshelved,
                Authors = new HashSet<string> { "Ursula K. Le Guin", "Frank Herbert" },
                Genres = new HashSet<string> { "Fantasy" },
                Series = new HashSet<string> { "Dune" },
                Acquired = new DateFilter(DateWindow.Custom, new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30)),
                LastFinished = new DateFilter(DateWindow.ThisYear),
            },
        },
    };

    [Fact]
    public void Round_trips_every_field()
    {
        var view = FullView();

        var json = LibraryViewJson.ToJson(view);
        var back = LibraryViewJson.FromJson(json);

        Assert.NotNull(back);
        Assert.True(LibraryViewJson.SameView(view, back));
        Assert.Equal(LibraryDisplay.Covers, back.Display);
        Assert.Equal(new LibrarySort(LibrarySortField.Color, true), back.Query.Sort);
        Assert.Equal(new DateFilter(DateWindow.Custom, new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30)), back.Filter.Acquired);
        Assert.True(back.Filter.Authors!.Contains("frank herbert")); // case-insensitive set
    }

    [Fact]
    public void Writes_enums_as_strings_and_omits_unset_filters()
    {
        var json = LibraryViewJson.ToJson(new LibraryView
        {
            Arrangement = LibraryArrangement.BySort,
            Query = new LibraryQuery
            {
                Filter = new LibraryFilter { Search = "dune", Statuses = new HashSet<OwnershipStatus> { OwnershipStatus.Wishlist } },
            },
        });

        Assert.Contains("\"arrangement\":\"BySort\"", json);
        Assert.Contains("\"statuses\":[\"Wishlist\"]", json);
        Assert.DoesNotContain("formats", json);
        Assert.DoesNotContain("search", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Default_sort_survives_a_round_trip()
    {
        var shelfOrder = LibraryView.Default.WithSort(new LibrarySort(LibrarySortField.ShelfOrder));

        var back = LibraryViewJson.FromJson(LibraryViewJson.ToJson(shelfOrder));

        Assert.Equal(LibrarySortField.ShelfOrder, back!.Query.Sort.Field);
    }

    [Fact]
    public void Ignores_unknown_properties_from_newer_versions()
    {
        const string json = """{"arrangement":"BySort","display":"Covers","sort":"Title","futureThing":{"x":1},"readState":"Read"}""";

        var view = LibraryViewJson.FromJson(json, schemaVersion: 2);

        Assert.NotNull(view);
        Assert.Equal(ReadState.Read, view.Filter.ReadState);
        Assert.Equal(LibrarySortField.Title, view.Query.Sort.Field);
    }

    [Theory]
    [InlineData("""{"display":"Hologram"}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Unreadable_json_returns_null(string json)
        => Assert.Null(LibraryViewJson.FromJson(json));

    [Fact]
    public void Missing_fields_mean_no_filter()
    {
        var view = LibraryViewJson.FromJson("{}");

        Assert.NotNull(view);
        Assert.Equal(0, view.Filter.ActiveCount);
        Assert.Equal(LibrarySortField.Author, view.Query.Sort.Field);
        Assert.Equal(LibraryArrangement.AsShelved, view.Arrangement);
    }

    [Fact]
    public void Same_view_ignores_search_and_set_order()
    {
        var a = LibraryView.Default.WithFilter(new LibraryFilter
        {
            Search = "dune",
            Authors = new HashSet<string> { "B", "a" },
        });
        var b = LibraryView.Default.WithFilter(new LibraryFilter
        {
            Authors = new HashSet<string> { "a", "B" },
        });

        Assert.True(LibraryViewJson.SameView(a, b));
        Assert.False(LibraryViewJson.SameView(a, LibraryView.Default));
    }
}
