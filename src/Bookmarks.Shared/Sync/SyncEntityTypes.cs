using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bookmarks.Shared.Serialization;

namespace Bookmarks.Shared.Sync;

/// <summary>One kind of synced record: its wire name and source-generated JSON metadata.</summary>
public sealed class SyncEntityType
{
    internal SyncEntityType(string name, Type clrType, JsonTypeInfo typeInfo)
    {
        Name = name;
        ClrType = clrType;
        TypeInfo = typeInfo;
    }

    /// <summary>The value of <see cref="SyncChange.EntityType"/>. Never rename: devices store it in their outbox.</summary>
    public string Name { get; }

    public Type ClrType { get; }

    public JsonTypeInfo TypeInfo { get; }

    public JsonElement ToPayload(SyncEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return JsonSerializer.SerializeToElement(entity, TypeInfo);
    }

    /// <exception cref="JsonException">The payload isn't a valid record of this type.</exception>
    public SyncEntity FromPayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"The {Name} payload must be a JSON object.");
        }

        return payload.Deserialize(TypeInfo) as SyncEntity
            ?? throw new JsonException($"The {Name} payload could not be read.");
    }

    public override string ToString() => Name;
}

/// <summary>Every synced entity type. Shared by the API and the client sync engine so both use the same names and JSON.</summary>
public static class SyncEntityTypes
{
    private static readonly BookmarksJsonContext Json = BookmarksJsonContext.Default;

    public static SyncEntityType Book { get; } = Create("book", Json.Book);

    public static SyncEntityType LibraryItem { get; } = Create("libraryItem", Json.LibraryItem);

    public static SyncEntityType ReadingSession { get; } = Create("readingSession", Json.ReadingSession);

    public static SyncEntityType Note { get; } = Create("note", Json.Note);

    public static SyncEntityType Tag { get; } = Create("tag", Json.Tag);

    public static SyncEntityType Bookcase { get; } = Create("bookcase", Json.Bookcase);

    public static SyncEntityType Shelf { get; } = Create("shelf", Json.Shelf);

    public static SyncEntityType Preset { get; } = Create("preset", Json.Preset);

    // Declared after the properties above: static initializers run in textual order.
    public static IReadOnlyList<SyncEntityType> All { get; } =
        [Book, LibraryItem, ReadingSession, Note, Tag, Bookcase, Shelf, Preset];

    private static readonly Dictionary<string, SyncEntityType> ByName = All.ToDictionary(t => t.Name, StringComparer.Ordinal);

    private static readonly Dictionary<Type, SyncEntityType> ByClrType = All.ToDictionary(t => t.ClrType);

    public static bool TryGet(string? name, out SyncEntityType type)
    {
        if (name is not null && ByName.TryGetValue(name, out var found))
        {
            type = found;
            return true;
        }

        type = null!;
        return false;
    }

    public static SyncEntityType Of<T>()
        where T : SyncEntity
        => Of(typeof(T));

    public static SyncEntityType Of(Type clrType)
        => ByClrType.TryGetValue(clrType, out var type)
            ? type
            : throw new NotSupportedException($"{clrType.Name} is not a synced entity type.");

    private static SyncEntityType Create<T>(string name, JsonTypeInfo<T> typeInfo)
        where T : SyncEntity
        => new(name, typeof(T), typeInfo);
}
