using Bookmarks.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Auth;

/// <summary>
/// <see cref="IAccountService"/> for the PWA, over MSAL in <c>wwwroot/js/auth.js</c>.
/// </summary>
/// <remarks>
/// <see cref="InitializeAsync"/> runs once before the app renders (see Program.cs) and only reads the
/// cached account, so an offline start keeps the user signed in and opens their library database.
/// Sign-in and sign-out navigate away and the app restarts afterwards.
/// </remarks>
public sealed class BrowserAccountService(IJSRuntime js, ClientAuthOptions options, ILogger<BrowserAccountService> logger)
    : IAccountService, IAsyncDisposable
{
    private const string ModulePath = "./js/auth.js";

    private IJSObjectReference? _module;

    public AccountInfo Current { get; private set; } = AccountInfo.Guest;

    public bool IsSignInSupported => options.IsConfigured;

    public event Action? Changed;

    /// <summary>Loads MSAL, finishes a sign-in redirect if there is one, and adopts the cached account.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!options.IsConfigured)
        {
            logger.LogInformation("Sign-in is not configured; running in guest mode.");
            return;
        }

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", ct, new object?[] { ModulePath });
            var account = await _module.InvokeAsync<string?[]?>(
                "init",
                ct,
                new object?[] { options.ClientId, options.Authority, options.KnownAuthority, options.RedirectUri, options.ApiScope });

            SetAccount(account);
            logger.LogInformation("Signed in: {SignedIn}", !Current.IsGuest);
        }
        catch (JSException ex)
        {
            // Never block startup on sign-in: the library works offline either way.
            logger.LogError(ex, "Sign-in could not be initialized; continuing as guest.");
            _module = null;
        }
    }

    public async Task SignInAsync(CancellationToken ct = default)
    {
        var module = _module ?? throw new InvalidOperationException("Sign-in is not available.");
        await module.InvokeVoidAsync("signIn", ct, Array.Empty<object?>());
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var module = _module ?? throw new InvalidOperationException("Sign-in is not available.");
        await module.InvokeVoidAsync("signOut", ct, Array.Empty<object?>());
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_module is null || Current.IsGuest)
        {
            return null;
        }

        try
        {
            return await _module.InvokeAsync<string?>("getToken", ct, Array.Empty<object?>());
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Could not get an access token.");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is going away; nothing to release.
            }
        }
    }

    /// <summary>auth.js returns [userId, displayName, email], or null when signed out.</summary>
    private void SetAccount(string?[]? account)
    {
        var next = account is { Length: 3 } && Guid.TryParse(account[0], out var userId) && userId != Guid.Empty
            ? new AccountInfo(userId, Blank(account[1]), Blank(account[2]))
            : AccountInfo.Guest;

        if (next == Current)
        {
            return;
        }

        Current = next;
        Changed?.Invoke();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
