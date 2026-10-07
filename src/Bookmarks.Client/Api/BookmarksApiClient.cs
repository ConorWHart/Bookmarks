using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Bookmarks.Shared.Api;
using Bookmarks.Shared.Serialization;
using Bookmarks.UI.Services;
using Microsoft.Extensions.Logging;

namespace Bookmarks.Client.Api;

/// <summary>
/// Calls the Bookmarks API as the signed-in user. Phase 3 only needs <c>GET /api/me</c>, which proves
/// the whole chain works: cached account, silent token, CORS, token validation, database.
/// Phase 4 adds the sync endpoints here.
/// </summary>
public sealed class BookmarksApiClient(HttpClient http, IAccountService accounts, ILogger<BookmarksApiClient> logger)
    : IAccountProbe
{
    public async Task<MeResponse?> GetMeAsync(CancellationToken ct = default)
    {
        var token = await accounts.GetAccessTokenAsync(ct);
        if (token is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, BookmarksJsonContext.Default.MeResponse);
    }

    /// <summary>A friendly "is my account reachable?" check for the account menu.</summary>
    public async Task<AccountProbeResult> CheckAsync(CancellationToken ct = default)
    {
        if (accounts.Current.IsGuest)
        {
            return new AccountProbeResult(false, "You're not signed in.");
        }

        try
        {
            var me = await GetMeAsync(ct);
            if (me is null)
            {
                return new AccountProbeResult(false, "Couldn't get a sign-in token. Sign out and back in to reconnect.");
            }

            var name = string.IsNullOrWhiteSpace(me.DisplayName) ? "your account" : me.DisplayName;
            return new AccountProbeResult(true, $"Connected as {name}. Your library syncs in a later update.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "The API could not be reached.");
            return ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new AccountProbeResult(false, "The service didn't accept your sign-in. Sign out and back in."),
                HttpStatusCode.Forbidden => new AccountProbeResult(false, "Your sign-in is missing permission for this app."),
                _ => new AccountProbeResult(false, "Couldn't reach the Bookmarks service. Your library is safe on this device."),
            };
        }
        catch (TaskCanceledException)
        {
            return new AccountProbeResult(false, "The Bookmarks service didn't answer in time.");
        }
    }
}
