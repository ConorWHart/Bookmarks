using System.Globalization;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Sync;

/// <summary>What one sync pass did.</summary>
/// <param name="Pushed">Local changes the server accepted.</param>
/// <param name="Rejected">Local changes that lost to a newer write (the winner arrives in the same pass's pull).</param>
/// <param name="Pulled">Records received from the server.</param>
/// <param name="LocalDataChanged">True when the pull changed local records, so the UI should reload.</param>
public sealed record SyncResult(int Pushed, int Rejected, int Pulled, bool LocalDataChanged, long Checkpoint);

/// <summary>
/// One sync pass for the open account database: push the outbox, then pull everything after the checkpoint.
/// See <c>claude/phase4-plan.md</c>. Not browser-specific; the scheduler that decides when to run it lives in the client.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Push takes a snapshot of the outbox, so edits made during a pass wait for the next one instead of keeping it going.</item>
/// <item>Push before pull: a rejected change's winner then always has a version above the checkpoint and arrives in this pass.</item>
/// <item>The checkpoint is saved after every pulled page, so an interrupted pull resumes where it stopped.</item>
/// <item>The first pass of a database (no checkpoint) queues every never-synced record, which uploads a library created
/// before sync existed; on a brand-new device it is simply a full download.</item>
/// <item>A failure (thrown by the transport) leaves the outbox and checkpoint consistent; just run again later.</item>
/// </list>
/// </remarks>
public sealed class SyncEngine(ILocalStore store, ISyncTransport transport, TimeProvider time)
{
    /// <summary>Meta key: the pull checkpoint (server rowversion as a decimal string). Absent until the first pass.</summary>
    public const string CheckpointKey = "sync:checkpoint";

    /// <summary>Meta key: this installation's id, sent with pushes for server logs.</summary>
    public const string DeviceIdKey = "sync:deviceId";

    /// <summary>Meta key: when the last pass completed (ISO 8601, UTC).</summary>
    public const string LastSyncedAtKey = "sync:lastSyncedAt";

    /// <summary>
    /// Meta key: the entity type names this database last completed a pull with. Older app versions skip
    /// types they don't know while their checkpoint moves on, so when the list changes (a new type was added)
    /// the next pass pulls from the start once. Re-applying known rows is a no-op (versions are compared).
    /// </summary>
    public const string KnownTypesKey = "sync:types";

    private static readonly string KnownTypes = string.Join(",", SyncEntityTypes.All.Select(t => t.Name));

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Records per pull request.</summary>
    public int PullPageSize { get; init; } = SyncLimits.DefaultPullLimit;

    /// <summary>Changes per push request.</summary>
    public int PushBatchSize { get; init; } = SyncLimits.MaxPushBatch;

    /// <summary>Runs one pass. Concurrent calls on the same engine run one after the other.</summary>
    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await RunAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>When the last pass completed on this device, or null if it never has.</summary>
    public async Task<DateTimeOffset?> GetLastSyncedAtAsync(CancellationToken ct = default)
        => DateTimeOffset.TryParse(
            await store.GetMetaAsync(LastSyncedAtKey, ct),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var value)
            ? value
            : null;

    private async Task<SyncResult> RunAsync(CancellationToken ct)
    {
        var checkpointText = await store.GetMetaAsync(CheckpointKey, ct);
        if (!long.TryParse(checkpointText, NumberStyles.None, CultureInfo.InvariantCulture, out var checkpoint))
        {
            checkpoint = 0;
            await store.EnqueueUnsyncedAsync(ct);
        }

        else if (await store.GetMetaAsync(KnownTypesKey, ct) != KnownTypes)
        {
            checkpoint = 0; // pull everything once; see KnownTypesKey
        }

        var deviceId = await GetDeviceIdAsync(ct);
        var (pushed, rejected) = await PushAsync(deviceId, ct);
        var (pulled, changed, newCheckpoint) = await PullAsync(checkpoint, ct);
        await store.SetMetaAsync(KnownTypesKey, KnownTypes, ct);

        await store.SetMetaAsync(LastSyncedAtKey, time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), ct);
        return new SyncResult(pushed, rejected, pulled, changed, newCheckpoint);
    }

    private async Task<(int Pushed, int Rejected)> PushAsync(Guid deviceId, CancellationToken ct)
    {
        var outbox = await store.GetOutboxAsync(ct);
        int pushed = 0, rejected = 0;

        foreach (var chunk in outbox.Chunk(PushBatchSize))
        {
            var batch = new List<(OutboxEntry Entry, SyncChange Change)>(chunk.Length);
            foreach (var entry in chunk)
            {
                if (!SyncEntityTypes.TryGet(entry.EntityType, out var type))
                {
                    continue; // Written by a newer version of the app; leave it for that version.
                }

                var entity = await store.GetForSyncAsync(type, entry.Id, ct);
                if (entity is null)
                {
                    await store.CompletePushAsync(entry, null, ct); // Nothing left to push.
                    continue;
                }

                batch.Add((entry, new SyncChange(
                    type.Name,
                    entity.Id,
                    entity.Version,
                    entity.UpdatedAt,
                    entity.IsDeleted,
                    type.ToPayload(entity))));
            }

            if (batch.Count == 0)
            {
                continue;
            }

            var response = await transport.PushAsync(new SyncPushRequest(deviceId, batch.Select(b => b.Change).ToList()), ct);
            if (response.Results.Count != batch.Count)
            {
                throw new InvalidOperationException(
                    $"The server answered {response.Results.Count} of {batch.Count} pushed changes.");
            }

            for (var i = 0; i < batch.Count; i++)
            {
                var result = response.Results[i];
                if (result.Accepted)
                {
                    await store.CompletePushAsync(batch[i].Entry, result.Version, ct);
                    pushed++;
                }
                else
                {
                    await store.CompletePushAsync(batch[i].Entry, null, ct);
                    rejected++;
                }
            }
        }

        return (pushed, rejected);
    }

    private async Task<(int Pulled, bool Changed, long Checkpoint)> PullAsync(long since, CancellationToken ct)
    {
        var pulled = 0;
        var changed = false;

        while (true)
        {
            var page = await transport.PullAsync(since, PullPageSize, ct);

            foreach (var change in page.Changes)
            {
                if (!SyncEntityTypes.TryGet(change.EntityType, out var type))
                {
                    continue; // A type this version of the app doesn't know yet.
                }

                // The envelope is authoritative; the payload's copies of these may be stale.
                var entity = type.FromPayload(change.Payload);
                entity.Id = change.Id;
                entity.Version = change.BaseVersion;
                entity.UpdatedAt = change.UpdatedAt;
                entity.IsDeleted = change.IsDeleted;

                changed |= await store.ApplyPulledAsync(type, entity, ct);
                pulled++;
            }

            var progressed = page.Checkpoint > since;
            since = Math.Max(since, page.Checkpoint);
            await store.SetMetaAsync(CheckpointKey, since.ToString(CultureInfo.InvariantCulture), ct);

            if (!page.HasMore || !progressed)
            {
                return (pulled, changed, since);
            }
        }
    }

    private async Task<Guid> GetDeviceIdAsync(CancellationToken ct)
    {
        if (Guid.TryParse(await store.GetMetaAsync(DeviceIdKey, ct), out var id))
        {
            return id;
        }

        id = Guid.CreateVersion7();
        await store.SetMetaAsync(DeviceIdKey, id.ToString("D"), ct);
        return id;
    }
}
