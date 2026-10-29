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

    /// <summary>
    /// The waiting version was found just as the app started or came back to the screen, so nothing is in progress
    /// and it can be applied straight away (Phase 8b).
    /// </summary>
    bool UpdateIsFresh { get; }

    /// <summary>Running as the installed app (from the home screen or its own window), not in a browser tab.</summary>
    bool IsInstalled { get; }

    /// <summary>The browser can show its own install prompt (Chrome, Edge, Samsung Internet): see <see cref="PromptInstallAsync"/>.</summary>
    bool CanPromptInstall { get; }

    /// <summary>Installed from this browser tab just now (the tab itself is still a browser tab).</summary>
    bool JustInstalled { get; }

    /// <summary>The browser and device, for the install steps.</summary>
    InstallPlatform Platform { get; }

    event Action? Changed;

    /// <summary>Begins watching. Safe to call more than once.</summary>
    Task StartAsync();

    /// <summary>Switches to the waiting version and reloads.</summary>
    Task ApplyUpdateAsync();

    /// <summary>Looks for a new version now.</summary>
    Task<UpdateCheckResult> CheckForUpdateAsync();

    /// <summary>Restarts the app (Settings → Reload app; the installed app has no browser reload).</summary>
    Task ReloadAsync();

    /// <summary>Shows the browser's install prompt; true when the user installed.</summary>
    Task<bool> PromptInstallAsync();
}

public sealed class NullAppStatusService : IAppStatusService
{
    public bool IsOnline => true;

    public bool UpdateAvailable => false;

    public bool UpdateIsFresh => false;

    // Hosts without a browser (MAUI, tests) count as installed: no install prompt.
    public bool IsInstalled => true;

    public bool CanPromptInstall => false;

    public bool JustInstalled => false;

    public InstallPlatform Platform => InstallPlatform.Unknown;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task StartAsync() => Task.CompletedTask;

    public Task ApplyUpdateAsync() => Task.CompletedTask;

    public Task<UpdateCheckResult> CheckForUpdateAsync() => Task.FromResult(UpdateCheckResult.Unsupported);

    public Task ReloadAsync() => Task.CompletedTask;

    public Task<bool> PromptInstallAsync() => Task.FromResult(false);
}
