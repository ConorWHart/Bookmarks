namespace Bookmarks.Core.Sync;

/// <summary>
/// When a device syncs on its own (lazy sync, 2026-11-01). The server's database is serverless: every time it wakes it
/// stays up ~15 minutes and that costs the same however little was synced, so passes are rare and batched:
/// <list type="bullet">
/// <item>a device that has never synced (just signed in) syncs straight away;</item>
/// <item>otherwise at least once every <see cref="PullEvery"/> (picks up changes made on other devices);</item>
/// <item>and when there are local changes, at most about once every <see cref="PushEvery"/>, or sooner once
/// <see cref="PushBatch"/> of them have piled up.</item>
/// </list>
/// Sync now (Settings), signing out and erasing the device always sync, whatever this says.
/// </summary>
public static class LazySyncPolicy
{
    public static readonly TimeSpan PullEvery = TimeSpan.FromDays(7);

    public static readonly TimeSpan PushEvery = TimeSpan.FromDays(1);

    public const int PushBatch = 20;

    /// <summary>Whether a background pass should run now.</summary>
    /// <param name="lastSyncedAt">When this device last completed a pass (null: never).</param>
    /// <param name="pendingChanges">Local changes not on the server yet.</param>
    /// <param name="now">The current time.</param>
    public static bool IsDue(DateTimeOffset? lastSyncedAt, int pendingChanges, DateTimeOffset now)
    {
        if (lastSyncedAt is not { } last)
        {
            return true;
        }

        var since = now - last;
        if (since < TimeSpan.Zero)
        {
            return true; // the clock went back: don't wait a week for it to catch up
        }

        if (since >= PullEvery)
        {
            return true;
        }

        return pendingChanges > 0 && (since >= PushEvery || pendingChanges >= PushBatch);
    }
}
