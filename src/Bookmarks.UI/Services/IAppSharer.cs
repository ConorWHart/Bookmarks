namespace Bookmarks.UI.Services;

/// <summary>How a share attempt ended.</summary>
public enum ShareOutcome
{
    /// <summary>The device's share sheet took the link (the user picked an app or contact).</summary>
    Shared,

    /// <summary>The user closed the share sheet without sharing.</summary>
    Cancelled,

    /// <summary>No share sheet here (or it refused), so the link was copied to the clipboard instead.</summary>
    Copied,

    /// <summary>Neither sharing nor copying worked.</summary>
    Failed,
}

/// <summary>
/// Hands the app's link to the device: its share sheet where there is one (phones, Windows, macOS Safari), otherwise the
/// clipboard. The PWA implements it with <c>navigator.share</c> and <c>navigator.clipboard</c>.
/// </summary>
public interface IAppSharer
{
    /// <summary>False on hosts with nothing to share (MAUI, tests): the menu item and Settings section are hidden.</summary>
    bool IsSupported { get; }

    /// <summary>Whether the device has a share sheet. Also loads what sharing needs, so call it ahead of the first tap:
    /// browsers only allow a share sheet right after a tap.</summary>
    Task<bool> CanShareNativelyAsync(CancellationToken ct = default);

    /// <summary>Opens the share sheet, falling back to copying <paramref name="url"/>.</summary>
    Task<ShareOutcome> ShareAsync(string title, string text, string url, CancellationToken ct = default);

    /// <summary>Copies <paramref name="text"/> to the clipboard. Returns whether it worked.</summary>
    Task<bool> CopyAsync(string text, CancellationToken ct = default);
}

public sealed class NullAppSharer : IAppSharer
{
    public bool IsSupported => false;

    public Task<bool> CanShareNativelyAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<ShareOutcome> ShareAsync(string title, string text, string url, CancellationToken ct = default)
        => Task.FromResult(ShareOutcome.Failed);

    public Task<bool> CopyAsync(string text, CancellationToken ct = default) => Task.FromResult(false);
}
