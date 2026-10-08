using System.Buffers.Binary;
using Bookmarks.Api.Auth;
using Bookmarks.Api.Data;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bookmarks.Api.Sync;

/// <summary>
/// Push and pull for the signed-in user's records. See <c>claude/phase4-plan.md</c> for the protocol.
/// </summary>
/// <remarks>
/// <para><b>Push</b>, per record: a new id is inserted; a row whose version still equals the device's
/// <see cref="SyncChange.BaseVersion"/> is overwritten (nobody else changed it); otherwise the later
/// <see cref="SyncChange.UpdatedAt"/> wins (last writer wins). One batch is one transaction, and the update uses the
/// row's version as a concurrency token, so a write that lands between our read and our save makes the batch retry.</para>
/// <para><b>Pull</b>: rows with <c>since &lt; Version &lt; MIN_ACTIVE_ROWVERSION()</c>, oldest first, tombstones
/// included. The upper bound matters: a transaction that is still open may already hold a lower rowversion than rows
/// committed after it, and returning those later rows would move the device's checkpoint past it for good.</para>
/// </remarks>
public sealed class SyncService(BookmarksDbContext db, ICurrentUser user, TimeProvider time, ILogger<SyncService> logger)
{
    private const int MaxPushAttempts = 3;

    private static readonly Dictionary<string, ISyncTable> Tables = new ISyncTable[]
    {
        new SyncTable<Book>(SyncEntityTypes.Book),
        new SyncTable<LibraryItem>(SyncEntityTypes.LibraryItem),
        new SyncTable<ReadingSession>(SyncEntityTypes.ReadingSession),
        new SyncTable<Note>(SyncEntityTypes.Note),
        new SyncTable<Tag>(SyncEntityTypes.Tag),
        new SyncTable<Bookcase>(SyncEntityTypes.Bookcase),
        new SyncTable<Shelf>(SyncEntityTypes.Shelf),
        new SyncTable<Preset>(SyncEntityTypes.Preset),
    }.ToDictionary(t => t.Type.Name, StringComparer.Ordinal);

    /// <summary>True when the server stores records of this <see cref="SyncChange.EntityType"/>.</summary>
    public static bool IsSynced(string? entityType) => entityType is not null && Tables.ContainsKey(entityType);

    /// <summary>
    /// Applies a validated batch (known types, no duplicate records). Results are in request order.
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException">A payload isn't a valid record of its type.</exception>
    public async Task<SyncPushResponse> PushAsync(IReadOnlyList<SyncChange> changes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var userId = user.RequiredId();
        var latestAllowed = time.GetUtcNow() + SyncLimits.MaxClockSkew;

        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var batch = new PushBatch(userId, latestAllowed);

            foreach (var group in changes.GroupBy(c => c.EntityType, StringComparer.Ordinal))
            {
                await Tables[group.Key].StageAsync(db, group.ToList(), batch, ct);
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (attempt < MaxPushAttempts)
            {
                // Another device wrote one of these rows (or inserted the same new id) after we read it.
                // Start over: the re-read sees the other write and the rules above decide again.
                logger.LogInformation(ex, "Sync push for {UserId} raced another write; retrying (attempt {Attempt}).", userId, attempt);
                continue;
            }

            // EF has read back the new rowversions of the rows it wrote.
            foreach (var (change, entity) in batch.Written)
            {
                batch.Results[Key(change)] = new SyncPushResult(change.EntityType, change.Id, true, entity.Version);
            }

            logger.LogDebug(
                "Sync push for {UserId}: {Written} written, {Rejected} rejected.",
                userId,
                batch.Written.Count,
                changes.Count - batch.Written.Count);

            return new SyncPushResponse(changes.Select(c => batch.Results[Key(c)]).ToList());
        }
    }

    /// <summary>The caller's changes after <paramref name="since"/>, at most <paramref name="limit"/>.</summary>
    public async Task<SyncPullResponse> PullAsync(long since, int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(since);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        user.RequiredId(); // The Owner query filter scopes every table to this user.

        var upper = await MinActiveRowVersionAsync(ct);

        // rowversion is database-wide, so one checkpoint covers every table. Taking limit + 1 from each table and
        // merging is enough to find the overall oldest `limit` and to know whether more remain.
        var rows = new List<(SyncEntityType Type, SyncEntity Entity)>();
        foreach (var table in Tables.Values)
        {
            foreach (var entity in await table.ChangesAsync(db, since, upper, limit + 1, ct))
            {
                rows.Add((table.Type, entity));
            }
        }

        var page = rows.OrderBy(r => r.Entity.Version).Take(limit + 1).ToList();
        var hasMore = page.Count > limit;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var changes = page
            .Select(r => new SyncChange(
                r.Type.Name,
                r.Entity.Id,
                r.Entity.Version,
                r.Entity.UpdatedAt,
                r.Entity.IsDeleted,
                r.Type.ToPayload(r.Entity)))
            .ToList();

        var checkpoint = page.Count > 0 ? page[^1].Entity.Version : since;
        return new SyncPullResponse(changes, checkpoint, hasMore);
    }

    /// <summary>The lowest rowversion any open transaction could still commit (the next one if none are open).</summary>
    private async Task<long> MinActiveRowVersionAsync(CancellationToken ct)
    {
        var values = await db.Database
            .SqlQueryRaw<byte[]>("SELECT MIN_ACTIVE_ROWVERSION() AS [Value]")
            .ToListAsync(ct);

        // binary(8), big-endian, the same encoding EF uses for SyncEntity.Version.
        return BinaryPrimitives.ReadInt64BigEndian(values.Single());
    }

    private static (string, Guid) Key(SyncChange change) => (change.EntityType, change.Id);

    private sealed class PushBatch(Guid userId, DateTimeOffset latestAllowed)
    {
        public Guid UserId { get; } = userId;

        public DateTimeOffset LatestAllowed { get; } = latestAllowed;

        /// <summary>Rejected changes; accepted ones are added after the save, when their versions are known.</summary>
        public Dictionary<(string, Guid), SyncPushResult> Results { get; } = [];

        public List<(SyncChange Change, SyncEntity Entity)> Written { get; } = [];
    }

    private interface ISyncTable
    {
        SyncEntityType Type { get; }

        Task StageAsync(BookmarksDbContext db, IReadOnlyList<SyncChange> changes, PushBatch batch, CancellationToken ct);

        Task<IReadOnlyList<SyncEntity>> ChangesAsync(BookmarksDbContext db, long since, long upper, int take, CancellationToken ct);
    }

    private sealed class SyncTable<T>(SyncEntityType type) : ISyncTable
        where T : SyncEntity
    {
        public SyncEntityType Type => type;

        public async Task StageAsync(BookmarksDbContext db, IReadOnlyList<SyncChange> changes, PushBatch batch, CancellationToken ct)
        {
            var ids = changes.Select(c => c.Id).ToList();

            // Every filter off: tombstones must be found (a restore is an update), and so must another user's row
            // with the same id, so it's refused instead of failing the batch on the primary key.
            var existing = await db.Set<T>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

            foreach (var change in changes)
            {
                var incoming = (T)type.FromPayload(change.Payload);
                incoming.Id = change.Id;
                incoming.UserId = batch.UserId; // Never trust the payload's owner.
                incoming.IsDeleted = change.IsDeleted;
                incoming.UpdatedAt = change.UpdatedAt > batch.LatestAllowed ? batch.LatestAllowed : change.UpdatedAt;

                if (!existing.TryGetValue(change.Id, out var row))
                {
                    incoming.Version = 0;
                    db.Set<T>().Add(incoming);
                    batch.Written.Add((change, incoming));
                    continue;
                }

                if (row.UserId != batch.UserId)
                {
                    // Someone else's record: don't touch it and don't reveal anything about it.
                    batch.Results[(change.EntityType, change.Id)] = new SyncPushResult(change.EntityType, change.Id, false, 0);
                    continue;
                }

                var unchangedSinceDeviceSawIt = row.Version == change.BaseVersion;
                if (!unchangedSinceDeviceSawIt && incoming.UpdatedAt <= row.UpdatedAt)
                {
                    // A newer write is already stored. The device gets it on its next pull.
                    batch.Results[(change.EntityType, change.Id)] = new SyncPushResult(change.EntityType, change.Id, false, row.Version);
                    continue;
                }

                // Overwrite every column; the version we read is the concurrency token (WHERE Version = @read).
                incoming.Version = row.Version;
                db.Set<T>().Update(incoming);
                batch.Written.Add((change, incoming));
            }
        }

        public async Task<IReadOnlyList<SyncEntity>> ChangesAsync(BookmarksDbContext db, long since, long upper, int take, CancellationToken ct)
            => await db.Set<T>()
                .IgnoreQueryFilters([BookmarksDbContext.NotDeletedFilter]) // Owner filter stays on.
                .AsNoTracking()
                .Where(x => x.Version > since && x.Version < upper)
                .OrderBy(x => x.Version)
                .Take(take)
                .ToListAsync(ct);
    }
}
