using System.Globalization;

namespace Bookmarks.UI.Services;

/// <summary>What the sync indicator shows.</summary>
public enum SyncStatus
{
    /// <summary>Guest mode or a host without sync: nothing to show.</summary>
    Off,

    /// <summary>Everything on this device is on the server.</summary>
    Synced,

    Syncing,

    /// <summary>Local changes are waiting for the next pass (normally a few seconds away), or nothing has synced yet.</summary>
    Pending,

    Offline,

    /// <summary>Sync keeps failing, or the sign-in needs renewing. <see cref="ISyncService.Problem"/> says why.</summary>
    NeedsAttention,
}

/// <summary>
/// Keeps the signed-in account's library in sync in the background (Phase 4). The PWA implements it with
/// <c>BrowserSyncService</c>; guests and hosts without sync get <see cref="NullSyncService"/>.
/// </summary>
public interface ISyncService
{
    /// <summary>True for a signed-in account on a host that syncs.</summary>
    bool IsEnabled { get; }

    SyncStatus Status { get; }

    /// <summary>When this device last completed a sync, or null if it never has.</summary>
    DateTimeOffset? LastSyncedAt { get; }

    /// <summary>Local changes not on the server yet.</summary>
    int PendingChanges { get; }

    /// <summary>Why <see cref="SyncStatus.NeedsAttention"/>; null otherwise.</summary>
    string? Problem { get; }

    event Action? Changed;

    /// <summary>Starts background syncing (first pass right away). Safe to call more than once.</summary>
    Task StartAsync();

    /// <summary>Runs a pass now and waits for it (Sync now, sign-out). Returns false if it didn't complete.</summary>
    Task<bool> SyncNowAsync();
}

public sealed class NullSyncService : ISyncService
{
    public bool IsEnabled => false;

    public SyncStatus Status => SyncStatus.Off;

    public DateTimeOffset? LastSyncedAt => null;

    public int PendingChanges => 0;

    public string? Problem => null;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task StartAsync() => Task.CompletedTask;

    public Task<bool> SyncNowAsync() => Task.FromResult(false);
}

/// <summary>Removes the signed-in account's library from this device (sign-out option).</summary>
public interface ILocalLibraryEraser
{
    /// <summary>Deletes this account's local database. The app must navigate away or reload afterwards.</summary>
    Task EraseAsync(CancellationToken ct = default);
}

public sealed class NullLocalLibraryEraser : ILocalLibraryEraser
{
    public Task EraseAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Wording for the sync indicator and account menu.</summary>
public static class SyncText
{
    public static string Describe(ISyncService sync, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sync);
        return sync.Status switch
        {
            SyncStatus.Syncing => "Syncing…",
            SyncStatus.Offline => sync.PendingChanges > 0
                ? $"Offline. {Changes(sync.PendingChanges)} will sync when you're back online."
                : "Offline. Changes are saved on this device and sync when you're back online.",
            SyncStatus.NeedsAttention => sync.Problem ?? "Sync isn't working right now.",
            SyncStatus.Pending => sync.PendingChanges > 0
                ? $"{Changes(sync.PendingChanges)} waiting to sync."
                : "Not synced yet.",
            SyncStatus.Synced => sync.LastSyncedAt is { } at ? $"Synced {Ago(at, now)}." : "Synced.",
            _ => "Your library is stored on this device only.",
        };
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "on 8 Oct".</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} min ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours} h ago";
        }

        return "on " + at.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture);
    }

    private static string Changes(int count) => count == 1 ? "1 change" : $"{count} changes";
}
