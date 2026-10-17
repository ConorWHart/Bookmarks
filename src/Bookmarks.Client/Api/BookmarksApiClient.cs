using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bookmarks.Client.Auth;
using Bookmarks.Core.Sync;
using Bookmarks.Shared.Api;
using Bookmarks.Shared.Serialization;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Client.Api;

/// <summary>
/// Calls the Bookmarks API as the signed-in user, and is the sync engine's transport (<see cref="ISyncTransport"/>).
/// Every request carries the device session token (or, before one exists, a fresh Entra token); see
/// <see cref="BrowserAccountService.GetAccessTokenAsync"/>.
/// </summary>
/// <remarks>Failures are thrown: <see cref="HttpRequestException"/> (with the status code when the API answered)
/// or <see cref="SignInRequiredException"/> when no token could be obtained.
/// A device session the API refuses (401) is forgotten and the request retried once, which goes through Entra.</remarks>
public sealed class BookmarksApiClient(HttpClient http, BrowserAccountService accounts) : ISyncTransport
{
    private static readonly BookmarksJsonContext Json = BookmarksJsonContext.Default;

    public Task<MeResponse> GetMeAsync(CancellationToken ct = default)
        => SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "api/me"), Json.MeResponse, ct);

    public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = JsonSerializer.Serialize(request, Json.SyncPushRequest);
        return SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "api/sync/push")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            },
            Json.SyncPushResponse,
            ct);
    }

    public Task<SyncPullResponse> PullAsync(long since, int limit, CancellationToken ct = default)
        => SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, FormattableString.Invariant($"api/sync/pull?since={since}&limit={limit}")),
            Json.SyncPullResponse,
            ct);

    /// <summary>Sends a request built by <paramref name="build"/> (called again for the retry, since a request can only be sent once).</summary>
    private async Task<T> SendAsync<T>(Func<HttpRequestMessage> build, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await accounts.GetAccessTokenAsync(ct)
                ?? throw (accounts.NeedsSignIn
                    ? new SignInRequiredException("The sign-in session has ended; the user needs to sign in again.") { SessionEnded = true }
                    : new SignInRequiredException());

            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && BrowserAccountService.IsSessionToken(token))
            {
                // Signed out on the server or expired: drop it and try once more (a new session if Entra still can).
                await accounts.SessionRejectedAsync(token, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize(json, typeInfo)
                ?? throw new InvalidOperationException($"The service sent an empty {typeof(T).Name}.");
        }
    }
}

/// <summary>
/// No access token was available: offline while the token needed refreshing, or the sign-in session has ended
/// and the user has to sign in again.
/// </summary>
public sealed class SignInRequiredException : Exception
{
    /// <summary>True when the sign-in session ended (not just offline): only signing in again fixes it.</summary>
    public bool SessionEnded { get; init; }

    public SignInRequiredException()
        : base("No access token is available. The device may be offline, or the sign-in needs renewing.")
    {
    }

    public SignInRequiredException(string message)
        : base(message)
    {
    }

    public SignInRequiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
