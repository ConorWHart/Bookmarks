using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Storage;

/// <summary>
/// On-device storage. The PWA implements this over IndexedDB (Phase 2); MAUI will use SQLite.
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

    Task<IReadOnlyList<Book>> GetBooksAsync(CancellationToken ct = default);

    Task UpsertBookAsync(Book book, CancellationToken ct = default);
}
