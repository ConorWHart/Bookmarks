using System.Text.Json;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Serialization;

namespace Bookmarks.Tests;

/// <summary>The IndexedDB store depends on these JSON shapes (key paths and indexes in localStore.js).</summary>
public class SerializationTests
{
    private static readonly BookmarksJsonContext Json = BookmarksJsonContext.Default;

    [Fact]
    public void LibraryItem_round_trips_with_nested_appearance()
    {
        var item = new LibraryItem
        {
            BookId = Guid.CreateVersion7(),
            Status = OwnershipStatus.LentOut,
            Format = BookFormat.LeatherBound,
            ShelfId = Guid.CreateVersion7(),
            ShelfRank = "V",
            AcquiredOn = new DateOnly(2024, 5, 1),
            PurchasePrice = 19.99m,
            Rating = 4.5m,
            TagIds = [Guid.CreateVersion7()],
            Appearance = new SpineAppearance { SpineColor = "#112233", Style = SpineStyle.Ornate, FaceOut = true },
        };

        var json = JsonSerializer.Serialize(item, Json.LibraryItem);
        var back = JsonSerializer.Deserialize(json, Json.LibraryItem)!;

        Assert.Equal(item.Id, back.Id);
        Assert.Equal(item.ShelfId, back.ShelfId);
        Assert.Equal(OwnershipStatus.LentOut, back.Status);
        Assert.Equal(new DateOnly(2024, 5, 1), back.AcquiredOn);
        Assert.Equal(19.99m, back.PurchasePrice);
        Assert.Equal(item.TagIds, back.TagIds);
        Assert.Equal(SpineStyle.Ornate, back.Appearance.Style);
        Assert.True(back.Appearance.FaceOut);
    }

    [Fact]
    public void Uses_camelCase_names_and_string_enums()
    {
        var item = new LibraryItem { Format = BookFormat.Paperback, ShelfId = Guid.CreateVersion7() };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(item, Json.LibraryItem));
        var root = doc.RootElement;

        // Dexie key path and indexes.
        Assert.Equal(item.Id.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(item.ShelfId.Value.ToString("D"), root.GetProperty("shelfId").GetString());
        Assert.Equal(JsonValueKind.False, root.GetProperty("isDeleted").ValueKind);
        Assert.Equal("Paperback", root.GetProperty("format").GetString());
    }

    [Fact]
    public void Book_does_not_store_computed_properties()
    {
        var book = new Book { Title = "Dune", Authors = ["Frank Herbert"] };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(book, Json.Book));

        Assert.False(doc.RootElement.TryGetProperty("primaryAuthor", out _));
        Assert.Equal("Frank Herbert", JsonSerializer.Deserialize(doc.RootElement.GetRawText(), Json.Book)!.PrimaryAuthor);
    }

    [Fact]
    public void Lists_round_trip()
    {
        var shelves = new List<Shelf>
        {
            new() { Name = "Top", WidthMm = 800, SortRank = "V" },
            new() { Name = "Bottom", IsDeleted = true },
        };

        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(shelves, Json.ShelfList), Json.ShelfList)!;

        Assert.Equal(new[] { "Top", "Bottom" }, back.Select(s => s.Name).ToArray());
        Assert.True(back[1].IsDeleted);
    }

    [Fact]
    public async Task InMemory_meta_set_get_and_clear()
    {
        var store = new InMemoryLocalStore();

        Assert.Null(await store.GetMetaAsync("pref:x"));

        await store.SetMetaAsync("pref:x", "0.5");
        Assert.Equal("0.5", await store.GetMetaAsync("pref:x"));

        await store.SetMetaAsync("pref:x", null);
        Assert.Null(await store.GetMetaAsync("pref:x"));
    }
}
