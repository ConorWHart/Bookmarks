using System.Text.Json.Serialization;
using Bookmarks.Shared.Api;
using Bookmarks.Shared.Backup;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Shared.Serialization;

/// <summary>
/// Source-generated JSON metadata for every persisted and synced type (trimming-safe, no reflection).
/// Used by the IndexedDB store, the sync payloads (<c>SyncEntityTypes</c>) and the client's API calls.
/// </summary>
/// <remarks>
/// camelCase property names (so IndexedDB key paths are <c>id</c>, <c>bookId</c>, …) and enums as
/// strings (so reordering an enum can never silently change stored data).
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Book))]
[JsonSerializable(typeof(List<Book>), TypeInfoPropertyName = "BookList")]
[JsonSerializable(typeof(LibraryItem))]
[JsonSerializable(typeof(List<LibraryItem>), TypeInfoPropertyName = "LibraryItemList")]
[JsonSerializable(typeof(ReadingSession))]
[JsonSerializable(typeof(List<ReadingSession>), TypeInfoPropertyName = "ReadingSessionList")]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(List<Note>), TypeInfoPropertyName = "NoteList")]
[JsonSerializable(typeof(Tag))]
[JsonSerializable(typeof(List<Tag>), TypeInfoPropertyName = "TagList")]
[JsonSerializable(typeof(Bookcase))]
[JsonSerializable(typeof(List<Bookcase>), TypeInfoPropertyName = "BookcaseList")]
[JsonSerializable(typeof(Shelf))]
[JsonSerializable(typeof(List<Shelf>), TypeInfoPropertyName = "ShelfList")]
[JsonSerializable(typeof(ShelfPlacement))]
[JsonSerializable(typeof(List<ShelfPlacement>), TypeInfoPropertyName = "ShelfPlacementList")]
[JsonSerializable(typeof(Preset))]
[JsonSerializable(typeof(List<Preset>), TypeInfoPropertyName = "PresetList")]
[JsonSerializable(typeof(LibraryBackup))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(SyncPushRequest))]
[JsonSerializable(typeof(SyncPushResponse))]
[JsonSerializable(typeof(SyncPullResponse))]
public sealed partial class BookmarksJsonContext : JsonSerializerContext
{
}
