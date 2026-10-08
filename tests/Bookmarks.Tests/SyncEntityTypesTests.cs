using System.Text.Json;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Tests;

/// <summary>The sync wire names and payloads shared by the API and the client engine.</summary>
public class SyncEntityTypesTests
{
    [Fact]
    public void Every_type_has_a_unique_name_and_is_found_by_name_and_clr_type()
    {
        Assert.Equal(8, SyncEntityTypes.All.Count);
        Assert.Equal(SyncEntityTypes.All.Count, SyncEntityTypes.All.Select(t => t.Name).Distinct().Count());

        foreach (var type in SyncEntityTypes.All)
        {
            Assert.True(SyncEntityTypes.TryGet(type.Name, out var byName));
            Assert.Same(type, byName);
            Assert.Same(type, SyncEntityTypes.Of(type.ClrType));
        }

        Assert.False(SyncEntityTypes.TryGet("Book", out _)); // names are case-sensitive
        Assert.False(SyncEntityTypes.TryGet(null, out _));
    }

    [Fact]
    public void Book_is_a_synced_record_and_its_payload_round_trips()
    {
        var userId = Guid.CreateVersion7();
        var book = new Book { UserId = userId, Title = "Piranesi", Authors = ["Susanna Clarke"], Version = 42 };

        var payload = SyncEntityTypes.Of<Book>().ToPayload(book);
        Assert.Equal(userId.ToString("D"), payload.GetProperty("userId").GetString());
        Assert.Equal(42L, payload.GetProperty("version").GetInt64());

        var back = (Book)SyncEntityTypes.Book.FromPayload(payload);
        Assert.Equal(book.Id, back.Id);
        Assert.Equal(new[] { "Susanna Clarke" }, back.Authors);
    }

    [Fact]
    public void A_payload_that_is_not_an_object_is_rejected()
    {
        using var doc = JsonDocument.Parse("[1, 2]");
        Assert.Throws<JsonException>(() => SyncEntityTypes.Tag.FromPayload(doc.RootElement));
    }
}
