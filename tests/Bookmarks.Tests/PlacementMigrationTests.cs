using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests;

public class PlacementMigrationTests
{
    private readonly Shelf _top = new() { BookcaseId = Guid.CreateVersion7(), Name = "Top" };
    private readonly Shelf _bottom = new() { Name = "Bottom" };

    public PlacementMigrationTests() => _bottom.BookcaseId = _top.BookcaseId;

    [Fact]
    public void Moves_each_shelved_book_with_its_rank()
    {
        var shelved = new LibraryItem { ShelfId = _top.Id, ShelfRank = "G", UserId = Guid.CreateVersion7() };
        var loose = new LibraryItem();
        var onDeletedShelf = new LibraryItem { ShelfId = Guid.CreateVersion7(), ShelfRank = "V" };

        var plan = PlacementMigration.Plan([shelved, loose, onDeletedShelf], [_top, _bottom], []);

        var placement = Assert.Single(plan);
        Assert.Equal(_top.Id, placement.ShelfId);
        Assert.Equal(shelved.Id, placement.LibraryItemId);
        Assert.Equal("G", placement.Rank);
        Assert.Equal(shelved.UserId, placement.UserId);
        Assert.Equal(PlacementMigration.IdFor(shelved.Id, _top.Id), placement.Id);
    }

    [Fact]
    public void Ids_are_the_same_on_every_device_and_differ_per_shelf()
    {
        var item = Guid.CreateVersion7();

        Assert.Equal(PlacementMigration.IdFor(item, _top.Id), PlacementMigration.IdFor(item, _top.Id));
        Assert.NotEqual(PlacementMigration.IdFor(item, _top.Id), PlacementMigration.IdFor(item, _bottom.Id));
        Assert.Equal(8, PlacementMigration.IdFor(item, _top.Id).Version);
    }

    [Fact]
    public void A_removed_placement_is_not_brought_back()
    {
        var item = new LibraryItem { ShelfId = _top.Id, ShelfRank = "G" };
        var removed = new ShelfPlacement { Id = PlacementMigration.IdFor(item.Id, _top.Id), ShelfId = _top.Id, LibraryItemId = item.Id, IsDeleted = true };

        Assert.Empty(PlacementMigration.Plan([item], [_top], [removed]));
    }

    [Fact]
    public void A_book_already_in_that_bookcase_is_left_alone()
    {
        var item = new LibraryItem { ShelfId = _top.Id, ShelfRank = "G" };
        var newer = new ShelfPlacement { ShelfId = _bottom.Id, LibraryItemId = item.Id, Rank = "V" };

        Assert.Empty(PlacementMigration.Plan([item], [_top, _bottom], [newer]));
    }

    [Fact]
    public void Running_it_again_creates_nothing()
    {
        var item = new LibraryItem { ShelfId = _top.Id, ShelfRank = "G" };
        var first = PlacementMigration.Plan([item], [_top], []);

        Assert.Empty(PlacementMigration.Plan([item], [_top], first));
    }
}
