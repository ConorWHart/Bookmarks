using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bookmarks.Core.Sync;
using Bookmarks.Shared.Api;
using Bookmarks.Shared.Serialization;
using Bookmarks.Shared.Sync;
using Bookmarks.UI.Services;

namespace Bookmarks.Client.Api;

/// <summary>
/// Calls the Bookmarks API as the signed-in user, and is the sync engine's transport (<see cref="ISyncTransport"/>).
/// Every request carries a fresh access token from MSAL's cache (refreshed silently when needed).
/// </summary>
/// <remarks>Failures are thrown: <see cref="HttpRequestException"/> (with the status code when the API answered)
/// or <see cref="SignInRequiredException"/> when no token could be obtained.</remarks>
public sealed class BookmarksApiClient(HttpClient http, IAccountService accounts) : ISyncTransport
{
    private static readonly BookmarksJsonContext Json = BookmarksJsonContext.Default;

    public async Task<MeResponse?> GetMeAsync(CancellationToken ct = default)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, "api/me", ct);
        return await SendAsync(request, Json.MeResponse, ct);
    }

    public async Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = await AuthorizedAsync(HttpMethod.Post, "api/sync/push", ct);
        message.Content = new StringContent(JsonSerializer.Serialize(request, Json.SyncPushRequest), Encoding.UTF8, "application/json");
        return await SendAsync(message, Json.SyncPushResponse, ct);
    }

    public async Task<SyncPullResponse> PullAsync(long since, int limit, CancellationToken ct = default)
    {
        using var message = await AuthorizedAsync(HttpMethod.Get, FormattableString.Invariant($"api/sync/pull?since={since}&limit={limit}"), ct);
        return await SendAsync(message, Json.SyncPullResponse, ct);
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var token = await accounts.GetAccessTokenAsync(ct) ?? throw new SignInRequiredException();
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, typeInfo)
            ?? throw new InvalidOperationException($"The service sent an empty {typeof(T).Name}.");
    }
}

/// <summary>
/// No access token was available: offline while the token needed refreshing, or the sign-in session has ended
/// and the user has to sign in again.
/// </summary>
public sealed class SignInRequiredException : Exception
{
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
