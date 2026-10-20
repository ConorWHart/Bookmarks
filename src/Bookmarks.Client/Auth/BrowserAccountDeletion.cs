using System.Runtime.ExceptionServices;
using Bookmarks.Client.Api;
using Bookmarks.Client.Storage;
using Bookmarks.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Auth;

/// <summary>
/// Settings → Delete account for the PWA: <c>DELETE /api/me</c>, then this device's copy of the library, then sign-out.
/// </summary>
/// <remarks>
/// The server delete and the local erase run while holding the account's cross-tab sync lock (<c>js/sync.js</c>), so
/// no tab is half-way through pushing its outbox when the data goes; a push landing after the delete would recreate
/// rows. Erasing the database closes it in the other tabs too, so their later passes fail instead of pushing.
/// </remarks>
public sealed class BrowserAccountDeletion(
    IJSRuntime js,
    BookmarksApiClient api,
    IndexedDbLocalStore store,
    BrowserAccountService accounts,
    ILogger<BrowserAccountDeletion> logger)
    : IAccountDeletion
{
    private Exception? _error;
    private bool _serverDeleted;

    public bool IsSupported => accounts.IsSignInSupported && !accounts.Current.IsGuest;

    public async Task DeleteAccountAsync(CancellationToken ct = default)
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException("Only a signed-in account can be deleted.");
        }

        _error = null;
        _serverDeleted = false;

        using (var self = DotNetObjectReference.Create(this))
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", ct, new object?[] { "./js/sync.js" });
            await module.InvokeVoidAsync("withSyncLock", ct, new object?[] { store.DatabaseName, self, nameof(DeleteUnderLockAsync) });
        }

        // Exceptions don't survive the JS round trip intact: rethrow the original here.
        if (_error is { } error && !_serverDeleted)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        if (_error is not null)
        {
            // The account is gone on the server; only this device's copy couldn't be removed. Signing out still
            // matters more than the leftover database, which no one can sync any more.
            logger.LogError(_error, "The account was deleted, but its local database could not be removed.");
        }

        logger.LogInformation("Account data deleted; signing out.");

        // Revoking the device session gets a 401 now (it was deleted with the account), which sign-out ignores.
        try
        {
            await accounts.SignOutAsync(ct);
        }
        catch (JSException ex)
        {
            // The data is gone either way; don't report a failed deletion. Restart the app instead, which opens an
            // empty library for this (still signed-in) user.
            logger.LogError(ex, "Sign-out after deleting the account failed; reloading.");
            await js.InvokeVoidAsync("location.reload", CancellationToken.None, Array.Empty<object?>());
        }
    }

    /// <summary>Runs inside the sync lock. Must not throw: errors are kept for <see cref="DeleteAccountAsync"/>.</summary>
    [JSInvokable]
    public async Task DeleteUnderLockAsync()
    {
        try
        {
            await api.DeleteMeAsync();
            _serverDeleted = true;
            await store.EraseAsync();
        }
        catch (Exception ex)
        {
            _error = ex;
        }
    }
}
