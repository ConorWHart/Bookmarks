using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Serialization;
using Bookmarks.Shared.Sync;
using Bookmarks.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Storage;

/// <summary>
/// <see cref="ILocalStore"/> over IndexedDB, via Dexie.js in <c>wwwroot/js/localStore.js</c>.
/// </summary>
/// <remarks>
/// Records cross the interop boundary as JSON strings produced with <see cref="BookmarksJsonContext"/>
/// (source-generated, so trimming-safe); the JS side stores them as plain objects so they're
/// readable in DevTools and can be indexed. A getAll is a single interop call per table.
/// Operations that touch a record and the sync outbox together run in one IndexedDB transaction in JS.
/// </remarks>
public sealed class IndexedDbLocalStore(IJSRuntime js, ILogger<IndexedDbLocalStore> logger)
    : ILocalStore, IGuestLibraryTransfer, ILocalLibraryEraser, IAsyncDisposable
{
    private const string ModulePath = "./js/localStore.js";

    /// <summary>Database used while signed out. Each account gets "bookmarks-{userId}" instead.</summary>
    public const string GuestDatabaseName = "bookmarks";

    /// <summary>
    /// Which database to open. Set once at startup (Program.cs) from the cached account, before
    /// anything reads or writes; switching accounts restarts the app, so it never changes while open.
    /// </summary>
    public string DatabaseName { get; set; } = GuestDatabaseName;

    public static string DatabaseNameFor(Guid userId)
        => userId == Guid.Empty ? GuestDatabaseName : $"{GuestDatabaseName}-{userId:D}";

    /// <summary>The guest database never syncs, so its edits aren't queued.</summary>
    private bool QueuesChanges => DatabaseName != GuestDatabaseName;

    private static readonly BookmarksJsonContext Json = BookmarksJsonContext.Default;

    // Table names must match the Dexie schema in localStore.js.
    private static readonly Dictionary<Type, TableInfo> Tables = new()
    {
        [typeof(Book)] = new("books", Json.Book, Json.BookList),
        [typeof(LibraryItem)] = new("libraryItems", Json.LibraryItem, Json.LibraryItemList),
        [typeof(ReadingSession)] = new("readingSessions", Json.ReadingSession, Json.ReadingSessionList),
        [typeof(Note)] = new("notes", Json.Note, Json.NoteList),
        [typeof(Tag)] = new("tags", Json.Tag, Json.TagList),
        [typeof(Bookcase)] = new("bookcases", Json.Bookcase, Json.BookcaseList),
        [typeof(Shelf)] = new("shelves", Json.Shelf, Json.ShelfList),
        [typeof(Preset)] = new("presets", Json.Preset, Json.PresetList),
        [typeof(ShelfPlacement)] = new("shelfPlacements", Json.ShelfPlacement, Json.ShelfPlacementList),
    };

    private readonly SemaphoreSlim _openGate = new(1, 1);
    private IJSObjectReference? _module;

    /// <summary>Raised after a local edit has been saved and queued for sync (not for guests or pulled rows).</summary>
    public event Action? LocalChanged;

    public async Task<IReadOnlyList<T>> GetAllAsync<T>(bool includeDeleted = false, CancellationToken ct = default)
        where T : SyncEntity
    {
        var table = TableFor(typeof(T));
        var module = await GetModuleAsync(ct);
        var json = await module.InvokeAsync<string>("getAll", ct, new object?[] { table.Name, includeDeleted });
        return JsonSerializer.Deserialize(json, (JsonTypeInfo<List<T>>)table.List) ?? [];
    }

    public async Task<T?> GetAsync<T>(Guid id, CancellationToken ct = default)
        where T : SyncEntity
    {
        var table = TableFor(typeof(T));
        var module = await GetModuleAsync(ct);
        var json = await module.InvokeAsync<string?>("get", ct, new object?[] { table.Name, id.ToString("D") });
        return json is null ? null : JsonSerializer.Deserialize(json, (JsonTypeInfo<T>)table.Item);
    }

    public async Task UpsertAsync<T>(T entity, CancellationToken ct = default)
        where T : SyncEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        var table = TableFor(typeof(T));
        var json = JsonSerializer.Serialize(entity, (JsonTypeInfo<T>)table.Item);
        var entityType = QueuesChanges ? SyncEntityTypes.Of(typeof(T)).Name : null;
        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync("put", ct, new object?[] { table.Name, json, entityType });

        if (entityType is not null)
        {
            LocalChanged?.Invoke();
        }
    }

    public async Task UpsertManyAsync<T>(IReadOnlyCollection<T> entities, CancellationToken ct = default)
        where T : SyncEntity
    {
        ArgumentNullException.ThrowIfNull(entities);
        if (entities.Count == 0)
        {
            return;
        }

        var table = TableFor(typeof(T));
        var json = JsonSerializer.Serialize(entities as List<T> ?? entities.ToList(), (JsonTypeInfo<List<T>>)table.List);
        var entityType = QueuesChanges ? SyncEntityTypes.Of(typeof(T)).Name : null;
        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync("putMany", ct, new object?[] { table.Name, json, entityType });

        if (entityType is not null)
        {
            LocalChanged?.Invoke();
        }
    }

    public Task<IReadOnlyList<Book>> GetBooksAsync(CancellationToken ct = default)
        => GetAllAsync<Book>(includeDeleted: true, ct);

    public Task UpsertBookAsync(Book book, CancellationToken ct = default)
        => UpsertAsync(book, ct);

    public async Task<int> CountGuestRecordsAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        return await module.InvokeAsync<int>("countGuestRecords", ct, Array.Empty<object?>());
    }

    public async Task<int> CountGuestBooksAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        return await module.InvokeAsync<int>("countGuestBooks", ct, Array.Empty<object?>());
    }

    public async Task DiscardGuestLibraryAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync("discardGuestLibrary", ct, Array.Empty<object?>());
    }

    public async Task<GuestLibraryMoveResult> MoveGuestLibraryAsync(Guid userId, CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        var result = await module.InvokeAsync<int[]>("moveGuestLibrary", ct, new object?[] { userId.ToString("D") });
        return result is [var records, var duplicates] ? new GuestLibraryMoveResult(records, duplicates) : new GuestLibraryMoveResult(0, 0);
    }

    public async Task<string?> GetMetaAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var module = await GetModuleAsync(ct);
        return await module.InvokeAsync<string?>("getMeta", ct, new object?[] { key });
    }

    public async Task SetMetaAsync(string key, string? value, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync("setMeta", ct, new object?[] { key, value });
    }

    // ---------- Sync ----------

    public async Task<IReadOnlyList<OutboxEntry>> GetOutboxAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        var json = await module.InvokeAsync<string>("getOutbox", ct, Array.Empty<object?>());

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(e => new OutboxEntry(
                e.GetProperty("entityType").GetString() ?? "",
                e.GetProperty("id").GetGuid(),
                e.GetProperty("stamp").GetString() ?? ""))
            .ToList();
    }

    public async Task<int> CountOutboxAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        return await module.InvokeAsync<int>("countOutbox", ct, Array.Empty<object?>());
    }

    public async Task<int> EnqueueUnsyncedAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        return await module.InvokeAsync<int>("enqueueUnsynced", ct, Array.Empty<object?>());
    }

    public async Task<SyncEntity?> GetForSyncAsync(SyncEntityType type, Guid id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        var table = TableFor(type.ClrType);
        var module = await GetModuleAsync(ct);
        var json = await module.InvokeAsync<string?>("get", ct, new object?[] { table.Name, id.ToString("D") });
        return json is null ? null : JsonSerializer.Deserialize(json, type.TypeInfo) as SyncEntity;
    }

    public async Task CompletePushAsync(OutboxEntry entry, long? acceptedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!SyncEntityTypes.TryGet(entry.EntityType, out var type))
        {
            throw new NotSupportedException($"Unknown sync entity type '{entry.EntityType}'.");
        }

        var table = TableFor(type.ClrType);
        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync(
            "completePush",
            ct,
            new object?[] { table.Name, type.Name, entry.Id.ToString("D"), entry.Stamp, acceptedVersion });
    }

    public async Task<bool> ApplyPulledAsync(SyncEntityType type, SyncEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(entity);
        var table = TableFor(type.ClrType);
        var json = JsonSerializer.Serialize(entity, type.TypeInfo);
        var module = await GetModuleAsync(ct);
        return await module.InvokeAsync<bool>("applyPulled", ct, new object?[] { table.Name, type.Name, json });
    }

    /// <summary>Deletes this account's database (sign-out "Remove from this device"). Nothing can be read or written afterwards.</summary>
    public async Task EraseAsync(CancellationToken ct = default)
    {
        if (!QueuesChanges)
        {
            throw new InvalidOperationException("Only a signed-in account's library can be removed this way.");
        }

        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync("eraseDatabase", ct, new object?[] { false });
    }

    /// <summary>
    /// Deletes the database in use, the signed-out one included (Settings → Erase this device's data).
    /// Nothing can be read or written afterwards; the app reloads.
    /// </summary>
    public async Task EraseCurrentDatabaseAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync("eraseDatabase", ct, new object?[] { true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is going away; nothing to release.
            }
        }

        _openGate.Dispose();
    }

    private static TableInfo TableFor(Type type)
        => Tables.TryGetValue(type, out var table)
            ? table
            : throw new NotSupportedException($"No IndexedDB table is mapped for {type.Name}.");

    /// <summary>Imports the JS module and opens the database once, on first use.</summary>
    private async ValueTask<IJSObjectReference> GetModuleAsync(CancellationToken ct)
    {
        if (_module is not null)
        {
            return _module;
        }

        await _openGate.WaitAsync(ct);
        try
        {
            if (_module is null)
            {
                var module = await js.InvokeAsync<IJSObjectReference>("import", ct, new object?[] { ModulePath });
                var persistence = await module.InvokeAsync<string>("open", ct, new object?[] { DatabaseName });
                logger.LogInformation(
                    "IndexedDB '{Database}' opened. Persistent storage: {Persistence}", DatabaseName, persistence);
                _module = module;
            }

            return _module;
        }
        finally
        {
            _openGate.Release();
        }
    }

    private sealed record TableInfo(string Name, JsonTypeInfo Item, JsonTypeInfo List);
}
