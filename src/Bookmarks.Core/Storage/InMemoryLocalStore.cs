using System.Collections.Concurrent;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Storage;

/// <summary>
/// Volatile store used until the IndexedDB implementation lands, and in unit tests.
/// </summary>
public sealed class InMemoryLocalStore : ILocalStore
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, SyncEntity>> _entities = new();
    private readonly ConcurrentDictionary<Guid, Book> _books = new();

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
        => Task.FromResult<IReadOnlyList<Book>>(_books.Values.ToList());

    public Task UpsertBookAsync(Book book, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        _books[book.Id] = book;
        return Task.CompletedTask;
    }

    private ConcurrentDictionary<Guid, SyncEntity> Table<T>() => _entities.GetOrAdd(typeof(T), _ => new());
}
