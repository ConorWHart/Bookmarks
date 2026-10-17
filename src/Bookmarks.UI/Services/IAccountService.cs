namespace Bookmarks.UI.Services;

/// <summary>Who the library belongs to on this device.</summary>
/// <param name="UserId">The Entra object id, or <see cref="Guid.Empty"/> in guest mode.</param>
public sealed record AccountInfo(Guid UserId, string? DisplayName, string? Email)
{
    /// <summary>No account: the library lives only on this device (the app works exactly as it did in Phase 2).</summary>
    public static readonly AccountInfo Guest = new(Guid.Empty, null, null);

    public bool IsGuest => UserId == Guid.Empty;

    /// <summary>Best available name for the account menu.</summary>
    public string Label => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
        : !string.IsNullOrWhiteSpace(Email) ? Email
        : "Signed in";
}

/// <summary>
/// Sign-in state. The app starts from the cached account with no network, so a guest and a
/// signed-in user both get a working library offline; a token is only needed to reach the API.
/// </summary>
public interface IAccountService
{
    AccountInfo Current { get; }

    /// <summary>False when the host can't sign in (MAUI for now, or a client with no Auth configuration).</summary>
    bool IsSignInSupported { get; }

    event Action? Changed;

    /// <summary>Starts the hosted sign-in page. The browser navigates away and the app restarts afterwards.</summary>
    Task SignInAsync(CancellationToken ct = default);

    /// <summary>Signs out and returns to guest mode. Library data stays on the device.</summary>
    Task SignOutAsync(CancellationToken ct = default);

    /// <summary>An access token for the API, or null when guest, offline, or re-authentication is needed.</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// True when the last <see cref="GetAccessTokenAsync"/> returned null because the sign-in session ended
    /// (not because the device is offline): only <see cref="RenewSignInAsync"/> fixes it.
    /// </summary>
    bool NeedsSignIn { get; }

    /// <summary>Signs in again as the same account (navigates away; the app restarts afterwards).</summary>
    Task RenewSignInAsync(CancellationToken ct = default);
}

/// <summary>Guest-only implementation for hosts without sign-in.</summary>
public sealed class GuestAccountService : IAccountService
{
    public AccountInfo Current => AccountInfo.Guest;

    public bool IsSignInSupported => false;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task SignInAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SignOutAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

    public bool NeedsSignIn => false;

    public Task RenewSignInAsync(CancellationToken ct = default) => Task.CompletedTask;
}
