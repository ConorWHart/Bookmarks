using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Sync;

/// <summary>
/// How the engine reaches the server: HTTP with the user's access token in the client, an in-memory fake in tests.
/// Failures (offline, server waking up, token expired) are thrown; the engine keeps the outbox and checkpoint as they are.
/// </summary>
public interface ISyncTransport
{
    /// <summary>POST /api/sync/push.</summary>
    Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken ct = default);

    /// <summary>GET /api/sync/pull?since={since}&amp;limit={limit}.</summary>
    Task<SyncPullResponse> PullAsync(long since, int limit, CancellationToken ct = default);
}
