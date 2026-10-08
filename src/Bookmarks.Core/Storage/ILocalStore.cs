using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Storage;

/// <summary>
/// On-device storage. The PWA implements this over IndexedDB (<c>IndexedDbLocalStore</c>); MAUI will use SQLite.
/// Reads include tombstoned records only when asked, because the sync engine needs them and the UI doesn't.
/// </summary>
public interface ILocalStore
{
    Task<IReadOnlyList<T>> GetAllAsync<T>(bool includeDeleted = false, CancellationToken ct = default)
        where T : SyncEntity;

    Task<T?> GetAsync<T>(Guid id, CancellationToken ct = default)
        where T : SyncEntity;

    Task UpsertAsync<T>(T entity, CancellationToken ct = default)
        where T : SyncEntity;

    /// <summary>Every book, including tombstones (a deleted item's book stays for Undo). Same as <c>GetAllAsync&lt;Book&gt;(true)</c>.</summary>
    Task<IReadOnlyList<Book>> GetBooksAsync(CancellationToken ct = default);

    /// <summary>Same as <c>UpsertAsync(book)</c>; kept for readability at call sites.</summary>
    Task UpsertBookAsync(Book book, CancellationToken ct = default);

    /// <summary>Device-local key/value data that never syncs: sync checkpoints, UI preferences.</summary>
    Task<string?> GetMetaAsync(string key, CancellationToken ct = default);

    /// <summary>Sets a meta value; null removes the key.</summary>
    Task SetMetaAsync(string key, string? value, CancellationToken ct = default);
}
