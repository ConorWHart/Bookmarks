using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Serialization;
using Bookmarks.Shared.Sync;
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
/// </remarks>
public sealed class IndexedDbLocalStore(IJSRuntime js, ILogger<IndexedDbLocalStore> logger) : ILocalStore, IAsyncDisposable
{
    private const string ModulePath = "./js/localStore.js";
    private const string BooksTable = "books";

    private static readonly BookmarksJsonContext Json = BookmarksJsonContext.Default;

    // Table names must match the Dexie schema in localStore.js.
    private static readonly Dictionary<Type, TableInfo> Tables = new()
    {
        [typeof(LibraryItem)] = new("libraryItems", Json.LibraryItem, Json.LibraryItemList),
        [typeof(ReadingSession)] = new("readingSessions", Json.ReadingSession, Json.ReadingSessionList),
        [typeof(Note)] = new("notes", Json.Note, Json.NoteList),
        [typeof(Tag)] = new("tags", Json.Tag, Json.TagList),
        [typeof(Bookcase)] = new("bookcases", Json.Bookcase, Json.BookcaseList),
        [typeof(Shelf)] = new("shelves", Json.Shelf, Json.ShelfList),
        [typeof(Preset)] = new("presets", Json.Preset, Json.PresetList),
    };

    private readonly SemaphoreSlim _openGate = new(1, 1);
    private IJSObjectReference? _module;

    public async Task<IReadOnlyList<T>> GetAllAsync<T>(bool includeDeleted = false, CancellationToken ct = default)
        where T : SyncEntity
    {
        var table = TableFor<T>();
        var module = await GetModuleAsync(ct);
        var json = await module.InvokeAsync<string>("getAll", ct, new object?[] { table.Name, includeDeleted });
        return JsonSerializer.Deserialize(json, (JsonTypeInfo<List<T>>)table.List) ?? [];
    }

    public async Task<T?> GetAsync<T>(Guid id, CancellationToken ct = default)
        where T : SyncEntity
    {
        var table = TableFor<T>();
        var module = await GetModuleAsync(ct);
        var json = await module.InvokeAsync<string?>("get", ct, new object?[] { table.Name, id.ToString("D") });
        return json is null ? null : JsonSerializer.Deserialize(json, (JsonTypeInfo<T>)table.Item);
    }

    public async Task UpsertAsync<T>(T entity, CancellationToken ct = default)
        where T : SyncEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        var table = TableFor<T>();
        var json = JsonSerializer.Serialize(entity, (JsonTypeInfo<T>)table.Item);
        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync("put", ct, new object?[] { table.Name, json });
    }

    public async Task<IReadOnlyList<Book>> GetBooksAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        var json = await module.InvokeAsync<string>("getAll", ct, new object?[] { BooksTable, true });
        return JsonSerializer.Deserialize(json, Json.BookList) ?? [];
    }

    public async Task UpsertBookAsync(Book book, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        var json = JsonSerializer.Serialize(book, Json.Book);
        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync("put", ct, new object?[] { BooksTable, json });
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

    private static TableInfo TableFor<T>()
        => Tables.TryGetValue(typeof(T), out var table)
            ? table
            : throw new NotSupportedException($"No IndexedDB table is mapped for {typeof(T).Name}.");

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
                var persistence = await module.InvokeAsync<string>("open", ct, Array.Empty<object?>());
                logger.LogInformation("IndexedDB opened. Persistent storage: {Persistence}", persistence);
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
