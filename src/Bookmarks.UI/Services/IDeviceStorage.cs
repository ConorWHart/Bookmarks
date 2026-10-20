namespace Bookmarks.UI.Services;

/// <summary>What this device stores for Bookmarks (Settings → This device). Any value the browser won't tell is null.</summary>
/// <param name="UsageBytes">Everything this site stores (databases, caches, the app itself), from <c>navigator.storage.estimate()</c>.</param>
/// <param name="QuotaBytes">How much the browser lets this site store.</param>
/// <param name="IsPersisted">True when the browser won't clear the data to free space; null when it can't say.</param>
/// <param name="CanRequestPersistence">The browser supports asking for persistent storage.</param>
/// <param name="CoverCount">Covers kept for offline use (published builds only; null when there's no cover cache).</param>
/// <param name="CoverBytes">Their total size.</param>
public sealed record DeviceStorageInfo(
    long? UsageBytes,
    long? QuotaBytes,
    bool? IsPersisted,
    bool CanRequestPersistence,
    int? CoverCount,
    long? CoverBytes);

/// <summary>Local storage housekeeping for the Settings page. The PWA implements it with the browser's storage APIs.</summary>
public interface IDeviceStorage
{
    /// <summary>False on hosts with nothing to show (MAUI, tests): the Settings page hides the section.</summary>
    bool IsSupported { get; }

    Task<DeviceStorageInfo> GetInfoAsync(CancellationToken ct = default);

    /// <summary>Asks the browser to keep this site's data. Returns whether it's persisted now.</summary>
    Task<bool> RequestPersistenceAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes the library database in use (the signed-in account's, or the signed-out one) and the cover cache.
    /// Sign-in stays as it is. The app must reload afterwards: nothing can be read or written until it does.
    /// </summary>
    Task EraseThisDeviceAsync(CancellationToken ct = default);

    /// <summary>Reloads the app (after erasing).</summary>
    Task ReloadAsync();
}

public sealed class NullDeviceStorage : IDeviceStorage
{
    public bool IsSupported => false;

    public Task<DeviceStorageInfo> GetInfoAsync(CancellationToken ct = default)
        => Task.FromResult(new DeviceStorageInfo(null, null, null, false, null, null));

    public Task<bool> RequestPersistenceAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task EraseThisDeviceAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task ReloadAsync() => Task.CompletedTask;
}
