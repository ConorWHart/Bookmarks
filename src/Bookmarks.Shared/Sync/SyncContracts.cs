using System.Text.Json;

namespace Bookmarks.Shared.Sync;

// Wire contracts for /api/sync/push and /api/sync/pull.
// Changes are sent as (entity type, JSON payload) pairs so new entity types don't need new endpoints.
// Payloads are the entity JSON from BookmarksJsonContext (camelCase, enums as strings); see SyncEntityTypes.

/// <summary>One changed record, in either direction.</summary>
/// <param name="EntityType">A <see cref="SyncEntityType.Name"/>, e.g. "libraryItem".</param>
/// <param name="Id">The record id (client-generated UUIDv7).</param>
/// <param name="BaseVersion">
/// Push: the server version the device last saw for this record (0 = never synced). If the server's row still has
/// this version, nobody else changed it and the write is accepted without comparing timestamps.
/// Pull: the record's current server version.
/// </param>
/// <param name="UpdatedAt">When the record was last changed on a device. Decides conflicts (last writer wins).</param>
/// <param name="IsDeleted">Tombstone. A delete is an ordinary write, and so is an undo.</param>
/// <param name="Payload">The full entity. The envelope's Id, UpdatedAt and IsDeleted win over the payload's, and the
/// server always takes the owner from the access token.</param>
public sealed record SyncChange(
    string EntityType,
    Guid Id,
    long BaseVersion,
    DateTimeOffset UpdatedAt,
    bool IsDeleted,
    JsonElement Payload);

/// <summary>POST /api/sync/push: up to <see cref="SyncLimits.MaxPushBatch"/> records from the device's outbox.</summary>
/// <param name="DeviceId">Identifies the installation in server logs only.</param>
public sealed record SyncPushRequest(Guid DeviceId, IReadOnlyList<SyncChange> Changes);

/// <summary>Per-record result of a push, in request order.</summary>
/// <param name="Accepted">
/// True: the server stored this change and <paramref name="Version"/> is its new version.
/// False: a newer write won (or the id belongs to someone else). The device drops it from its outbox and gets the
/// winning copy on the next pull; <paramref name="Version"/> is the server's current version (0 if not visible).
/// </param>
public sealed record SyncPushResult(string EntityType, Guid Id, bool Accepted, long Version);

public sealed record SyncPushResponse(IReadOnlyList<SyncPushResult> Results);

/// <summary>
/// GET /api/sync/pull?since={checkpoint}&amp;limit={n}: the caller's records with Version &gt; since, oldest first,
/// tombstones included. Store <paramref name="Checkpoint"/> and call again while <paramref name="HasMore"/> is true.
/// </summary>
public sealed record SyncPullResponse(IReadOnlyList<SyncChange> Changes, long Checkpoint, bool HasMore);

public static class SyncLimits
{
    /// <summary>Most changes accepted by one push (one database transaction).</summary>
    public const int MaxPushBatch = 200;

    public const int DefaultPullLimit = 500;

    public const int MaxPullLimit = 1000;

    /// <summary>
    /// How far ahead of the server's clock a device's <see cref="SyncChange.UpdatedAt"/> may be. Later timestamps are
    /// clamped, so a device with a wrong clock can't win every conflict.
    /// </summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);
}
