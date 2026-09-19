using System.Text.Json;

namespace Bookmarks.Shared.Sync;

// Wire contracts for the sync endpoints (implemented in Phase 4).
// Changes are sent as (entity type, JSON payload) pairs so new entity types
// don't require new endpoints.

/// <summary>One changed record, in either direction.</summary>
public sealed record SyncChange(string EntityType, Guid Id, DateTimeOffset UpdatedAt, bool IsDeleted, JsonElement Payload);

/// <summary>POST /api/sync/push: the client's outbox.</summary>
public sealed record SyncPushRequest(Guid DeviceId, IReadOnlyList<SyncChange> Changes);

/// <summary>Per-record result of a push. Rejected = server had a newer write (client should re-pull).</summary>
public sealed record SyncPushResult(Guid Id, bool Accepted, long Version);

public sealed record SyncPushResponse(IReadOnlyList<SyncPushResult> Results);

/// <summary>GET /api/sync/pull?since={checkpoint}: everything with Version &gt; checkpoint.</summary>
public sealed record SyncPullResponse(IReadOnlyList<SyncChange> Changes, long Checkpoint, bool HasMore);
