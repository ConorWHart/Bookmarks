using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Bookmarks.UI.Services;

/// <summary>
/// "Share Bookmarks", shared by the side menu and Settings: sends the app's link through the device's share sheet, or
/// copies it, with the matching message. Only the app's address is shared, never anything from the library.
/// </summary>
public sealed class ShareAppActions(IAppSharer sharer, NavigationManager navigation, ISnackbar snackbar)
{
    private const string ShareTitle = "Bookmarks";
    private const string ShareText = "Bookmarks: your physical library, on every device. Try it in your browser and install it from there.";

    private bool? _canShareNatively;

    public bool IsSupported => sharer.IsSupported;

    /// <summary>The device has a share sheet (known after <see cref="PrepareAsync"/>; false until then).</summary>
    public bool CanShareNatively => _canShareNatively == true;

    /// <summary>The app's home page: the address someone else opens to try and install Bookmarks.</summary>
    public string AppUrl => navigation.BaseUri;

    /// <summary>Loads the share code ahead of the first tap. Safe to call more than once.</summary>
    public async Task PrepareAsync()
    {
        if (_canShareNatively is not null || !sharer.IsSupported)
        {
            return;
        }

        try
        {
            _canShareNatively = await sharer.CanShareNativelyAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _canShareNatively = false;
        }
    }

    /// <summary>The share sheet where there is one, otherwise copies the link.</summary>
    public async Task ShareAsync()
    {
        ShareOutcome outcome;
        try
        {
            outcome = await sharer.ShareAsync(ShareTitle, ShareText, AppUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome = ShareOutcome.Failed;
        }

        switch (outcome)
        {
            case ShareOutcome.Copied:
                snackbar.Add("Link copied. Paste it into a message to share Bookmarks.", Severity.Success);
                break;
            case ShareOutcome.Failed:
                snackbar.Add($"Couldn't share from here. The link is {AppUrl}", Severity.Warning);
                break;
        }
    }

    public async Task CopyLinkAsync()
    {
        bool copied;
        try
        {
            copied = await sharer.CopyAsync(AppUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            copied = false;
        }

        if (copied)
        {
            snackbar.Add("Link copied.", Severity.Success);
        }
        else
        {
            snackbar.Add("Couldn't copy the link. Select it above and copy it instead.", Severity.Warning);
        }
    }
}
