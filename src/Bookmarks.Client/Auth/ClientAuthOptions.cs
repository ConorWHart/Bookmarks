namespace Bookmarks.Client.Auth;

/// <summary>
/// Sign-in settings from <c>wwwroot/appsettings.json</c> ("Auth" section). None of these are secrets:
/// they identify the app registration in the Bookmarks Entra External ID tenant.
/// </summary>
public sealed class ClientAuthOptions
{
    /// <summary>e.g. https://bookmarksapp.ciamlogin.com/{tenantId}/v2.0</summary>
    public string Authority { get; init; } = "";

    /// <summary>e.g. https://bookmarksapp.ciamlogin.com (MSAL requires the host to be listed explicitly).</summary>
    public string KnownAuthority { get; init; } = "";

    /// <summary>Application (client) id of the "Bookmarks" SPA registration.</summary>
    public string ClientId { get; init; } = "";

    /// <summary>e.g. api://{apiClientId}/access_as_user</summary>
    public string ApiScope { get; init; } = "";

    /// <summary>Absolute URI registered on the app registration, built from the app's base address.</summary>
    public string RedirectUri { get; init; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ApiScope);
}
