namespace Bookmarks.UI.Services;

/// <summary>Result of Settings → Check for updates.</summary>
public enum UpdateCheckResult
{
    /// <summary>No service worker (development without one, MAUI, tests).</summary>
    Unsupported,

    /// <summary>This is the latest version.</summary>
    UpToDate,

    /// <summary>A new version was found and is downloading; Reload is offered when it's ready.</summary>
    Downloading,

    /// <summary>A new version is waiting; <see cref="IAppStatusService.UpdateAvailable"/> is now true.</summary>
    Ready,

    /// <summary>Offline or the server couldn't be reached.</summary>
    Failed,
}

/// <summary>
/// Connectivity and app-update state from the host. The PWA implements it with the browser
/// and its service worker; hosts without either (MAUI, tests) get <see cref="NullAppStatusService"/>.
/// </summary>
public interface IAppStatusService
{
    bool IsOnline { get; }

    /// <summary>A newer version has been downloaded and is waiting to take over.</summary>
    bool UpdateAvailable { get; }

    event Action? Changed;

    /// <summary>Begins watching. Safe to call more than once.</summary>
    Task StartAsync();

    /// <summary>Switches to the waiting version and reloads.</summary>
    Task ApplyUpdateAsync();

    /// <summary>Looks for a new version now.</summary>
    Task<UpdateCheckResult> CheckForUpdateAsync();
}

public sealed class NullAppStatusService : IAppStatusService
{
    public bool IsOnline => true;

    public bool UpdateAvailable => false;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task StartAsync() => Task.CompletedTask;

    public Task ApplyUpdateAsync() => Task.CompletedTask;

    public Task<UpdateCheckResult> CheckForUpdateAsync() => Task.FromResult(UpdateCheckResult.Unsupported);
}
