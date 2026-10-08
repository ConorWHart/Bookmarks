using System.Collections.Concurrent;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Storage;

/// <summary>
/// Volatile store for unit tests (and hosts without persistent storage).
/// </summary>
public sealed class InMemoryLocalStore : ILocalStore
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, SyncEntity>> _entities = new();
    private readonly ConcurrentDictionary<string, string> _meta = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<T>> GetAllAsync<T>(bool includeDeleted = false, CancellationToken ct = default)
        where T : SyncEntity
    {
        IReadOnlyList<T> result = Table<T>().Values
            .Cast<T>()
            .Where(e => includeDeleted || !e.IsDeleted)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<T?> GetAsync<T>(Guid id, CancellationToken ct = default)
        where T : SyncEntity
        => Task.FromResult(Table<T>().TryGetValue(id, out var e) ? (T)e : null);

    public Task UpsertAsync<T>(T entity, CancellationToken ct = default)
        where T : SyncEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        Table<T>()[entity.Id] = entity;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Book>> GetBooksAsync(CancellationToken ct = default)
        => GetAllAsync<Book>(includeDeleted: true, ct);

    public Task UpsertBookAsync(Book book, CancellationToken ct = default)
        => UpsertAsync(book, ct);

    public Task<string?> GetMetaAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_meta.TryGetValue(key, out var value) ? value : null);

    public Task SetMetaAsync(string key, string? value, CancellationToken ct = default)
    {
        if (value is null)
        {
            _meta.TryRemove(key, out _);
        }
        else
        {
            _meta[key] = value;
        }

        return Task.CompletedTask;
    }

    private ConcurrentDictionary<Guid, SyncEntity> Table<T>() => _entities.GetOrAdd(typeof(T), _ => new());
}
