namespace Bookmarks.UI.Services;

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
}
