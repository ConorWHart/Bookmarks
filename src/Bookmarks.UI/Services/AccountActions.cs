using Bookmarks.Core.Storage;
using Bookmarks.UI.Components;
using MudBlazor;

namespace Bookmarks.UI.Services;

/// <summary>
/// Account and device actions with their confirmations and messages, shared by the app-bar account menu and the
/// Settings page: sign in / create account, Sync now, sign out (with "remove from this device"), delete account,
/// and erase this device's data.
/// </summary>
/// <remarks>Sign-in, sign-out and account deletion navigate away; <see cref="IsBusy"/> stays true until they do.</remarks>
public sealed class AccountActions(
    IAccountService accounts,
    ISyncService sync,
    ILocalLibraryEraser localLibrary,
    IGuestLibraryTransfer guestLibrary,
    ILocalStore store,
    IAppStatusService appStatus,
    IAccountDeletion deletion,
    IDeviceStorage device,
    IDialogService dialogs,
    ISnackbar snackbar)
{
    private static readonly TimeSpan SyncBeforeLeavingTimeout = TimeSpan.FromSeconds(15);

    /// <summary>An action is running (or the page is about to navigate away).</summary>
    public bool IsBusy { get; private set; }

    public event Action? Changed;

    /// <summary>Starts the hosted sign-in page (the app restarts when it comes back).</summary>
    public Task SignInAsync() => StartAccountFlowAsync(accounts.SignInAsync);

    /// <summary>Like <see cref="SignInAsync"/>, opening on "create account".</summary>
    public Task SignUpAsync() => StartAccountFlowAsync(accounts.SignUpAsync);

    public async Task SyncNowAsync()
    {
        if (await sync.SyncNowAsync())
        {
            snackbar.Add(
                sync.PendingChanges == 0 ? "Your library is up to date." : SyncText.Describe(sync, DateTimeOffset.Now),
                Severity.Success);
        }
        else
        {
            snackbar.Add(sync.Problem ?? "Couldn't sync right now. It will try again shortly.", Severity.Warning);
        }
    }

    public async Task SignOutAsync()
    {
        // Give unsynced changes a chance to reach the server first, so "remove from this device" can be offered.
        await SyncBeforeLeavingAsync();

        var parameters = new DialogParameters
        {
            { nameof(SignOutDialog.PendingChanges), sync.PendingChanges },
            { nameof(SignOutDialog.HasSynced), sync.LastSyncedAt is not null },
        };
        var dialog = await dialogs.ShowAsync<SignOutDialog>(null, parameters, new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true });
        var result = await dialog.Result;
        if (result is null || result.Canceled)
        {
            return;
        }

        var remove = result.Data is true;
        SetBusy(true);
        try
        {
            if (remove)
            {
                // Re-check right before deleting: an edit could have landed since the dialog opened.
                if (await store.CountOutboxAsync() > 0)
                {
                    SetBusy(false);
                    snackbar.Add("A change hasn't synced yet, so your library was kept on this device. Try again in a moment.", Severity.Warning);
                    return;
                }

                await localLibrary.EraseAsync();

                // "Remove my library from this device" means all of it, including any library from before
                // signing in, so the signed-out view starts empty.
                await guestLibrary.DiscardGuestLibraryAsync();
            }

            await accounts.SignOutAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetBusy(false);
            snackbar.Add("Sign-out failed. Please try again.", Severity.Error);
        }
    }

    /// <summary>Settings → Delete account: confirm by typing DELETE, then delete on the server, here, and sign out.</summary>
    public async Task DeleteAccountAsync()
    {
        if (!deletion.IsSupported || accounts.Current.IsGuest)
        {
            return;
        }

        if (!appStatus.IsOnline)
        {
            snackbar.Add("You're offline. Deleting your account needs a connection.", Severity.Warning);
            return;
        }

        var parameters = new DialogParameters
        {
            { nameof(DeleteAccountDialog.AccountName), accounts.Current.Email ?? accounts.Current.Label },
        };
        var dialog = await dialogs.ShowAsync<DeleteAccountDialog>(null, parameters, new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true });
        var result = await dialog.Result;
        if (result is null || result.Canceled)
        {
            return;
        }

        SetBusy(true);
        try
        {
            // Signs out at the end, which navigates away; IsBusy stays set until then.
            await deletion.DeleteAccountAsync();
        }
        catch (HttpRequestException)
        {
            SetBusy(false);
            snackbar.Add("Couldn't reach the Bookmarks service, so nothing was deleted. Please try again.", Severity.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetBusy(false);
            snackbar.Add("Your account couldn't be deleted, so nothing was removed. Please try again.", Severity.Error);
        }
    }

    /// <summary>
    /// Settings → Erase this device's data: deletes the library database in use and the cover cache, then reloads.
    /// For an account the library comes back from the server on the next sync; unsynced changes are warned about first.
    /// </summary>
    public async Task EraseThisDeviceAsync()
    {
        if (!device.IsSupported)
        {
            return;
        }

        var guest = accounts.Current.IsGuest;
        if (!guest)
        {
            await SyncBeforeLeavingAsync();
        }

        var pending = 0;
        if (!guest)
        {
            try
            {
                pending = await store.CountOutboxAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                pending = sync.PendingChanges;
            }
        }

        var message = EraseMessage(guest, pending, hasSynced: sync.LastSyncedAt is not null);
        var confirmed = await dialogs.ShowMessageBoxAsync(
            "Erase this device's data?", message, yesText: "Erase", cancelText: "Cancel");
        if (confirmed != true)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await device.EraseThisDeviceAsync();
            await device.ReloadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetBusy(false);
            snackbar.Add("Couldn't erase this device's data. Close other Bookmarks tabs and try again.", Severity.Error);
        }
    }

    private static string EraseMessage(bool guest, int pending, bool hasSynced)
    {
        if (guest)
        {
            return "This deletes every book, bookcase, reading session and note on this device, and its saved covers. "
                + "You're not signed in, so there's no other copy. This can't be undone.";
        }

        var lost = !hasSynced
            ? " Your library hasn't synced yet, so anything added on this device will be lost."
            : pending switch
            {
                0 => "",
                1 => " 1 change hasn't synced yet and will be lost.",
                _ => $" {pending} changes haven't synced yet and will be lost.",
            };

        return "Your library will be removed from this device, with its saved covers and settings, and downloaded again "
            + "from your account. You stay signed in." + lost;
    }

    private async Task SyncBeforeLeavingAsync()
    {
        if (sync.IsEnabled && appStatus.IsOnline && (sync.PendingChanges > 0 || sync.LastSyncedAt is null))
        {
            SetBusy(true);
            try
            {
                await Task.WhenAny(sync.SyncNowAsync(), Task.Delay(SyncBeforeLeavingTimeout));
            }
            finally
            {
                SetBusy(false);
            }
        }
    }

    private async Task StartAccountFlowAsync(Func<CancellationToken, Task> start)
    {
        SetBusy(true);
        try
        {
            // Navigates to the hosted sign-in page; the app restarts when it comes back.
            await start(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetBusy(false);
            snackbar.Add("Sign-in couldn't start. Please try again.", Severity.Error);
        }
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        Changed?.Invoke();
    }
}
