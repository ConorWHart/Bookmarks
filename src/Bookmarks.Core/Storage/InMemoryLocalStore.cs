using System.Collections.Concurrent;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Storage;

/// <summary>
/// Volatile store for unit tests (and hosts without persistent storage).
/// </summary>
/// <remarks>
/// Stores the instances it's given (no copies), like a cache would. Operations that touch a record and the outbox
/// together take one lock, matching the IndexedDB store's transactions.
/// </remarks>
public sealed class InMemoryLocalStore : ILocalStore
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, SyncEntity>> _entities = new();
    private readonly ConcurrentDictionary<string, string> _meta = new(StringComparer.Ordinal);

    private readonly Lock _sync = new();
    private readonly Dictionary<(string Type, Guid Id), string> _outbox = []; // guarded by _sync

    public Task<IReadOnlyList<T>> GetAllAsync<T>(bool includeDeleted = false, CancellationToken ct = default)
        where T : SyncEntity
    {
        IReadOnlyList<T> result = Table(typeof(T)).Values
            .Cast<T>()
            .Where(e => includeDeleted || !e.IsDeleted)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<T?> GetAsync<T>(Guid id, CancellationToken ct = default)
        where T : SyncEntity
        => Task.FromResult(Table(typeof(T)).TryGetValue(id, out var e) ? (T)e : null);

    public Task UpsertAsync<T>(T entity, CancellationToken ct = default)
        where T : SyncEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        var type = SyncEntityTypes.Of(typeof(T));
        lock (_sync)
        {
            Table(typeof(T))[entity.Id] = entity;
            _outbox[(type.Name, entity.Id)] = NewStamp();
        }

        return Task.CompletedTask;
    }

    public Task UpsertManyAsync<T>(IReadOnlyCollection<T> entities, CancellationToken ct = default)
        where T : SyncEntity
    {
        ArgumentNullException.ThrowIfNull(entities);
        var type = SyncEntityTypes.Of(typeof(T));
        lock (_sync)
        {
            foreach (var entity in entities)
            {
                Table(typeof(T))[entity.Id] = entity;
                _outbox[(type.Name, entity.Id)] = NewStamp();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stores a record without queuing it, as if it had been there before sync existed (e.g. a library moved in from
    /// guest mode during Phase 3). For tests of the first sync.
    /// </summary>
    public void Seed<T>(T entity)
        where T : SyncEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        Table(typeof(T))[entity.Id] = entity;
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

    // ---------- Sync ----------

    public Task<IReadOnlyList<OutboxEntry>> GetOutboxAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            IReadOnlyList<OutboxEntry> entries = _outbox
                .Select(kv => new OutboxEntry(kv.Key.Type, kv.Key.Id, kv.Value))
                .ToList();
            return Task.FromResult(entries);
        }
    }

    public Task<int> CountOutboxAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_outbox.Count);
        }
    }

    public Task<int> EnqueueUnsyncedAsync(CancellationToken ct = default)
    {
        var queued = 0;
        lock (_sync)
        {
            foreach (var type in SyncEntityTypes.All)
            {
                foreach (var entity in Table(type.ClrType).Values.Where(e => e.Version == 0))
                {
                    _outbox[(type.Name, entity.Id)] = NewStamp();
                    queued++;
                }
            }
        }

        return Task.FromResult(queued);
    }

    public Task<SyncEntity?> GetForSyncAsync(SyncEntityType type, Guid id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Task.FromResult(Table(type.ClrType).TryGetValue(id, out var e) ? e : null);
    }

    public Task CompletePushAsync(OutboxEntry entry, long? acceptedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_sync)
        {
            var key = (entry.EntityType, entry.Id);
            if (_outbox.TryGetValue(key, out var stamp) && stamp == entry.Stamp)
            {
                _outbox.Remove(key);
            }

            if (acceptedVersion is { } version
                && SyncEntityTypes.TryGet(entry.EntityType, out var type)
                && Table(type.ClrType).TryGetValue(entry.Id, out var entity)
                && entity.Version < version)
            {
                entity.Version = version;
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> ApplyPulledAsync(SyncEntityType type, SyncEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(entity);

        lock (_sync)
        {
            var table = Table(type.ClrType);
            var key = (type.Name, entity.Id);

            if (!table.TryGetValue(entity.Id, out var local))
            {
                table[entity.Id] = entity;
                return Task.FromResult(true);
            }

            if (_outbox.ContainsKey(key))
            {
                if (entity.UpdatedAt > local.UpdatedAt)
                {
                    table[entity.Id] = entity;
                    _outbox.Remove(key);
                    return Task.FromResult(true);
                }

                // Our pending edit is newer: keep it queued, based on the version we've now seen.
                if (entity.Version > local.Version)
                {
                    local.Version = entity.Version;
                }

                return Task.FromResult(false);
            }

            if (local.Version >= entity.Version)
            {
                return Task.FromResult(false);
            }

            table[entity.Id] = entity;
            return Task.FromResult(true);
        }
    }

    private ConcurrentDictionary<Guid, SyncEntity> Table(Type type) => _entities.GetOrAdd(type, _ => new());

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
