using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;
using static Bookmarks.Tests.TestData;

namespace Bookmarks.Tests;

/// <summary>Filters added in Phase 6a.</summary>
public class LibraryFilterTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    private static bool Matches(LibraryEntry entry, LibraryFilter filter)
        => LibraryQueryEngine.Matches(entry, filter, Today);

    [Fact]
    public void Authors_genres_and_series_match_case_insensitively()
    {
        var entry = Entry("Dune", author: "Frank Herbert", series: "Dune", genre: "Science Fiction");

        Assert.True(Matches(entry, new LibraryFilter { Authors = new HashSet<string> { "frank herbert", "Someone" } }));
        Assert.True(Matches(entry, new LibraryFilter { Genres = new HashSet<string> { "SCIENCE FICTION" } }));
        Assert.True(Matches(entry, new LibraryFilter { Series = new HashSet<string> { "dune" } }));
        Assert.False(Matches(entry, new LibraryFilter { Series = new HashSet<string> { "Foundation" } }));
        Assert.False(Matches(Entry("Standalone"), new LibraryFilter { Series = new HashSet<string> { "Dune" } }));
    }

    [Fact]
    public void Bookcase_and_shelf_filters_and_unshelved()
    {
        var hall = Guid.NewGuid();
        var favorites = Guid.NewGuid();
        var hallShelf = Guid.NewGuid();
        var twice = Entry("Twice") with { ShelfIds = [hallShelf, Guid.NewGuid()], BookcaseIds = [hall, favorites] };
        var loose = Entry("Loose");

        Assert.True(Matches(twice, new LibraryFilter { BookcaseId = hall }));
        Assert.True(Matches(twice, new LibraryFilter { BookcaseId = favorites }));
        Assert.True(Matches(twice, new LibraryFilter { ShelfId = hallShelf }));
        Assert.False(Matches(loose, new LibraryFilter { BookcaseId = hall }));
        Assert.True(Matches(loose, new LibraryFilter { BookcaseId = LibraryFilter.Unshelved }));
        Assert.False(Matches(twice, new LibraryFilter { BookcaseId = LibraryFilter.Unshelved }));
    }

    [Fact]
    public void To_read_is_owned_unfinished_and_not_in_progress()
    {
        var reading = Entry("Reading");
        reading = LibraryEntry.Create(reading.Item, reading.Book, new[]
        {
            new ReadingSession { LibraryItemId = reading.Item.Id, Outcome = ReadingOutcome.InProgress },
        });
        var filter = new LibraryFilter { ReadState = ReadState.ToRead };

        Assert.True(Matches(Entry("Pile"), filter));
        Assert.True(Matches(Entry("Lent", status: OwnershipStatus.LentOut), filter));
        Assert.False(Matches(Entry("Wanted", status: OwnershipStatus.Wishlist), filter));
        Assert.False(Matches(Entry("Done", reads: 1), filter));
        Assert.False(Matches(reading, filter));
    }

    [Fact]
    public void Min_read_count()
    {
        Assert.True(Matches(Entry("Twice", reads: 2), new LibraryFilter { MinReadCount = 2 }));
        Assert.False(Matches(Entry("Once", reads: 1), new LibraryFilter { MinReadCount = 2 }));
    }

    [Fact]
    public void Acquired_uses_acquired_date_then_added_date()
    {
        var acquired = Entry("Acquired");
        acquired.Item.AcquiredOn = new DateOnly(2026, 3, 1);
        acquired.Item.AddedAt = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var added = Entry("Added");
        added.Item.AddedAt = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var thisYear = new LibraryFilter { Acquired = new DateFilter(DateWindow.ThisYear) };
        var lastYear = new LibraryFilter { Acquired = new DateFilter(DateWindow.LastYear) };

        Assert.True(Matches(acquired, thisYear));
        Assert.False(Matches(added, thisYear));
        Assert.True(Matches(added, lastYear));
    }

    [Fact]
    public void Last_finished_needs_a_finished_read()
    {
        var filter = new LibraryFilter { LastFinished = new DateFilter(DateWindow.Custom, new DateOnly(2023, 12, 1), new DateOnly(2024, 1, 31)) };

        Assert.True(Matches(Entry("Read", reads: 1), filter)); // TestData finishes on 2024-01-01
        Assert.False(Matches(Entry("Unread"), filter));
    }

    [Theory]
    [InlineData(DateWindow.Last30Days, 2026, 9, 12, true)]
    [InlineData(DateWindow.Last30Days, 2026, 9, 11, false)]
    [InlineData(DateWindow.Last90Days, 2026, 7, 14, true)]
    [InlineData(DateWindow.ThisYear, 2026, 1, 1, true)]
    [InlineData(DateWindow.LastYear, 2026, 1, 1, false)]
    [InlineData(DateWindow.Any, 1990, 1, 1, true)]
    public void Date_windows_are_inclusive(DateWindow window, int y, int m, int d, bool expected)
        => Assert.Equal(expected, new DateFilter(window).Matches(new DateOnly(y, m, d), Today));

    [Fact]
    public void Open_ended_custom_range_and_empty_custom_is_inactive()
    {
        Assert.True(new DateFilter(DateWindow.Custom, From: new DateOnly(2026, 1, 1)).Matches(new DateOnly(2030, 1, 1), Today));
        Assert.False(new DateFilter(DateWindow.Custom).IsActive);
        Assert.True(new DateFilter(DateWindow.Custom).Matches(null, Today));
    }

    [Fact]
    public void Active_count_ignores_search()
    {
        var filter = new LibraryFilter
        {
            Search = "x",
            ReadState = ReadState.Read,
            Genres = new HashSet<string> { "Fantasy" },
            Acquired = new DateFilter(DateWindow.Custom),
        };

        Assert.Equal(2, filter.ActiveCount);
    }
}
