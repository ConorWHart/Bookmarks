using System.Text.Json.Serialization;
using Bookmarks.Shared.Api;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Shared.Serialization;

/// <summary>
/// Source-generated JSON metadata for every persisted and synced type (trimming-safe, no reflection).
/// Used by the IndexedDB store today and by the sync payloads in Phase 4.
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
[JsonSerializable(typeof(Preset))]
[JsonSerializable(typeof(List<Preset>), TypeInfoPropertyName = "PresetList")]
[JsonSerializable(typeof(MeResponse))]
public sealed partial class BookmarksJsonContext : JsonSerializerContext
{
}
