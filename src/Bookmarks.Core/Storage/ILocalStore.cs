using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Storage;

/// <summary>
/// On-device storage. The PWA implements this over IndexedDB (<c>IndexedDbLocalStore</c>); MAUI will use SQLite.
/// Reads include tombstoned records only when asked, because the sync engine needs them and the UI doesn't.
/// </summary>
/// <remarks>
/// Every <see cref="UpsertAsync{T}"/> is a local edit: it also records the record in the <b>outbox</b>, in the same
/// transaction, so the sync engine knows what to push. Rows written by the sync engine itself
/// (<see cref="ApplyPulledAsync"/>, <see cref="CompletePushAsync"/>) don't enter the outbox.
/// </remarks>
public interface ILocalStore
{
    Task<IReadOnlyList<T>> GetAllAsync<T>(bool includeDeleted = false, CancellationToken ct = default)
        where T : SyncEntity;

    Task<T?> GetAsync<T>(Guid id, CancellationToken ct = default)
        where T : SyncEntity;

    /// <summary>Saves a local edit and queues it for sync.</summary>
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

    // ---------- Sync (used by SyncEngine) ----------

    /// <summary>Every record with a local change that hasn't been pushed yet (one entry per record).</summary>
    Task<IReadOnlyList<OutboxEntry>> GetOutboxAsync(CancellationToken ct = default);

    /// <summary>How many records are waiting to be pushed (0 = everything on this device has synced).</summary>
    Task<int> CountOutboxAsync(CancellationToken ct = default);

    /// <summary>
    /// Queues every record that has never been synced (<see cref="SyncEntity.Version"/> 0). Run before an account
    /// database's first sync, so a library created before sync existed gets uploaded. Returns how many were queued.
    /// </summary>
    Task<int> EnqueueUnsyncedAsync(CancellationToken ct = default);

    /// <summary>A record of any synced type, tombstones included.</summary>
    Task<SyncEntity?> GetForSyncAsync(SyncEntityType type, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Records the server's answer to a push, atomically: the outbox entry is removed only if its
    /// <see cref="OutboxEntry.Stamp"/> is unchanged (an edit made during the push stays queued), and when the change was
    /// accepted the record's <see cref="SyncEntity.Version"/> is raised to <paramref name="acceptedVersion"/>.
    /// Pass null for a rejected change: the winning copy arrives with the next pull.
    /// </summary>
    Task CompletePushAsync(OutboxEntry entry, long? acceptedVersion, CancellationToken ct = default);

    /// <summary>
    /// Stores a record from the server, atomically, without queuing it. Returns true when local data changed.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>New record → stored.</item>
    /// <item>No pending local edit → stored, unless the local copy is already at this version or later.</item>
    /// <item>Pending local edit that is newer (<see cref="SyncEntity.UpdatedAt"/>) → the local content stays queued, but its
    /// version is raised to the server's, so the next push is accepted (we've seen this version and our edit wins).</item>
    /// <item>Pending local edit that is older → the server's copy wins and the outbox entry is dropped.</item>
    /// </list>
    /// </remarks>
    Task<bool> ApplyPulledAsync(SyncEntityType type, SyncEntity entity, CancellationToken ct = default);
}

/// <summary>A record waiting to be pushed.</summary>
/// <param name="EntityType">A <see cref="SyncEntityType.Name"/>.</param>
/// <param name="Stamp">Changes on every local edit of the record; lets a push tell whether the record was edited meanwhile.</param>
public sealed record OutboxEntry(string EntityType, Guid Id, string Stamp);
