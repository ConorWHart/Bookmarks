namespace Bookmarks.UI.Services;

/// <summary>
/// Deletes the signed-in account's data (Settings → Delete account). Decision 2026-10-19: only Bookmarks' data goes
/// (every synced row, device sessions, the account row); the sign-in itself (the Entra user) is kept, so signing in
/// again with the same email starts an empty library.
/// </summary>
public interface IAccountDeletion
{
    /// <summary>False for guests and hosts without an API.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Deletes the account's data on the server, removes its library from this device and signs out (the browser
    /// navigates away). Throws if the server couldn't delete it; nothing on this device is touched in that case.
    /// </summary>
    Task DeleteAccountAsync(CancellationToken ct = default);
}

public sealed class NullAccountDeletion : IAccountDeletion
{
    public bool IsSupported => false;

    public Task DeleteAccountAsync(CancellationToken ct = default)
        => throw new NotSupportedException("Accounts can't be deleted on this host.");
}
