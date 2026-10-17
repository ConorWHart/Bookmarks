using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bookmarks.Shared.Api;
using Bookmarks.Shared.Serialization;
using Bookmarks.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Auth;

/// <summary>
/// <see cref="IAccountService"/> for the PWA, over MSAL in <c>wwwroot/js/auth.js</c>, plus our own device sessions.
/// </summary>
/// <remarks>
/// <para><see cref="InitializeAsync"/> runs once before the app renders (see Program.cs) and only reads the
/// cached account, so an offline start keeps the user signed in and opens their library database.
/// Sign-in and sign-out navigate away and the app restarts afterwards.</para>
/// <para>Device sessions (see <c>claude/device-sessions-plan.md</c>): Entra only keeps a single-page app signed in
/// for 24 hours, so the first Entra token is traded for a session token from our API (<c>bms_…</c>), which lasts
/// 90 days from the last sync and is swapped for a new one weekly. It's kept in localStorage per account, shared
/// by all tabs. Entra is only needed again when the session ends.</para>
/// </remarks>
public sealed class BrowserAccountService(
    IJSRuntime js,
    ClientAuthOptions options,
    HttpClient api,
    ILogger<BrowserAccountService> logger)
    : IAccountService, IAsyncDisposable
{
    private const string ModulePath = "./js/auth.js";

    private static readonly BookmarksJsonContext Json = BookmarksJsonContext.Default;

    // One token lookup at a time in this tab, so a sync pass doesn't create or rotate a session twice.
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private IJSObjectReference? _module;

    public AccountInfo Current { get; private set; } = AccountInfo.Guest;

    public bool IsSignInSupported => options.IsConfigured;

    public bool NeedsSignIn { get; private set; }

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

    public async Task RenewSignInAsync(CancellationToken ct = default)
    {
        var module = _module ?? throw new InvalidOperationException("Sign-in is not available.");
        await module.InvokeVoidAsync("renewSignIn", ct, Array.Empty<object?>());
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var module = _module ?? throw new InvalidOperationException("Sign-in is not available.");
        await RevokeSessionAsync(module, ct);
        await module.InvokeVoidAsync("signOut", ct, Array.Empty<object?>());
    }

    /// <summary>
    /// A token for the API: the device session if there is one (rotated when due), else a fresh Entra token
    /// traded for a new session. Null when guest, offline before a session exists, or the user has to sign in again
    /// (<see cref="NeedsSignIn"/>).
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_module is not { } module || Current.IsGuest)
        {
            return null;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (await ReadSessionAsync(module, ct) is { } session)
            {
                NeedsSignIn = false;
                return DateTimeOffset.UtcNow >= session.RotateAfter
                    ? await RotateAsync(module, session, ct)
                    : session.Token;
            }

            var entraToken = await GetEntraTokenAsync(module, ct);
            if (entraToken is null)
            {
                return null;
            }

            // If the API can't create a session right now (e.g. an older API version), use the Entra token for this call.
            return await CreateSessionAsync(module, entraToken, ct) ?? entraToken;
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Could not get an access token.");
            return null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>True for our own session tokens (as opposed to Entra access tokens).</summary>
    public static bool IsSessionToken(string token) => token.StartsWith(SessionTokens.Prefix, StringComparison.Ordinal);

    /// <summary>
    /// The API refused <paramref name="token"/> (signed out elsewhere, expired, or unknown): forget it, so the next
    /// <see cref="GetAccessTokenAsync"/> tries Entra (and asks the user to sign in again if that's needed too).
    /// </summary>
    public async Task SessionRejectedAsync(string token, CancellationToken ct = default)
    {
        if (_module is not { } module || !IsSessionToken(token))
        {
            return;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Another tab may already have stored a newer token; only forget this one.
            if (await ReadSessionAsync(module, ct) is { } stored && stored.Token == token)
            {
                logger.LogInformation("The device session was refused by the API; it will be replaced on the next sync.");
                await module.InvokeVoidAsync("clearSession", ct, Array.Empty<object?>());
            }
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Could not clear the device session.");
        }
        finally
        {
            _tokenLock.Release();
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

        _tokenLock.Dispose();
    }

    // ---------- Device sessions ----------

    private sealed record StoredSession(string Token, DateTimeOffset RotateAfter, DateTimeOffset ExpiresAt);

    /// <summary>The stored session for the current account, or null (none, unreadable, or already past its expiry).</summary>
    private async Task<StoredSession?> ReadSessionAsync(IJSObjectReference module, CancellationToken ct)
    {
        var stored = await module.InvokeAsync<string?[]?>("getSession", ct, Array.Empty<object?>());
        if (stored is not [{ Length: > 0 } token, var rotateText, var expiresText]
            || !TryParseDate(rotateText, out var rotateAfter)
            || !TryParseDate(expiresText, out var expiresAt))
        {
            return null;
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            await module.InvokeVoidAsync("clearSession", ct, Array.Empty<object?>());
            return null;
        }

        return new StoredSession(token, rotateAfter, expiresAt);
    }

    private static Task StoreSessionAsync(IJSObjectReference module, SessionResponse session, CancellationToken ct)
        => module.InvokeVoidAsync(
            "setSession",
            ct,
            new object?[]
            {
                session.Token,
                session.RotateAfter.ToString("O", CultureInfo.InvariantCulture),
                session.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            }).AsTask();

    /// <summary>An Entra access token from MSAL (refreshed silently if it can be); sets <see cref="NeedsSignIn"/>.</summary>
    private async Task<string?> GetEntraTokenAsync(IJSObjectReference module, CancellationToken ct)
    {
        var token = await module.InvokeAsync<string?>("getToken", ct, Array.Empty<object?>());
        NeedsSignIn = token is null && await module.InvokeAsync<bool>("needsSignIn", ct, Array.Empty<object?>());
        return token;
    }

    /// <summary>POST /api/sessions with the Entra token. Returns the new session token, or null if it couldn't be created.</summary>
    private async Task<string?> CreateSessionAsync(IJSObjectReference module, string entraToken, CancellationToken ct)
    {
        try
        {
            var deviceName = await module.InvokeAsync<string?>("deviceName", ct, Array.Empty<object?>());
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/sessions")
            {
                Content = JsonContent.Create(new CreateSessionRequest(deviceName), Json.CreateSessionRequest),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", entraToken);

            using var response = await api.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Could not start a device session ({Status}); using the Entra token for now.", (int)response.StatusCode);
                return null;
            }

            var session = await response.Content.ReadFromJsonAsync(Json.SessionResponse, ct);
            if (session is null)
            {
                return null;
            }

            await StoreSessionAsync(module, session, ct);
            logger.LogInformation("Device session started; signed in until {Expires:d} unless used again.", session.ExpiresAt);
            return session.Token;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not start a device session; using the Entra token for now.");
            return null;
        }
    }

    /// <summary>
    /// POST /api/sessions/rotate. Best effort: on a network error the current token is still good for days, so
    /// it's used as is and rotation is tried again on the next sync.
    /// </summary>
    private async Task<string?> RotateAsync(IJSObjectReference module, StoredSession session, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/sessions/rotate");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            using var response = await api.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.OK
                && await response.Content.ReadFromJsonAsync(Json.SessionResponse, ct) is { } rotated)
            {
                await StoreSessionAsync(module, rotated, ct);
                return rotated.Token;
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                // Another tab rotated first and stored the new token.
                return (await ReadSessionAsync(module, ct))?.Token ?? session.Token;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // The session ended on the server: forget it. The request then fails with 401 and the API client
                // retries through Entra.
                await module.InvokeVoidAsync("clearSession", ct, Array.Empty<object?>());
            }

            return session.Token;
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Session rotation will be retried later.");
            return session.Token;
        }
    }

    /// <summary>Sign-out: ends the session on the server (best effort, a few seconds at most) and forgets it here.</summary>
    private async Task RevokeSessionAsync(IJSObjectReference module, CancellationToken ct)
    {
        try
        {
            if (await ReadSessionAsync(module, ct) is { } session)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));

                using var request = new HttpRequestMessage(HttpMethod.Delete, "api/sessions/current");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
                using var response = await api.SendAsync(request, timeout.Token);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JSException)
        {
            // Offline or slow: the server's copy expires on its own after 90 days without use.
            logger.LogInformation(ex, "Could not end the device session on the server.");
        }

        try
        {
            await module.InvokeVoidAsync("clearSession", ct, Array.Empty<object?>());
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Could not clear the device session.");
        }
    }

    private static bool TryParseDate(string? text, out DateTimeOffset value)
        => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);

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
