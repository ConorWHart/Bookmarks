namespace Bookmarks.Shared.Api;

/// <summary>
/// POST /api/sessions: trade a fresh Entra access token for a long-lived device session, so the device stays
/// signed in for sync (Entra's single-page-app sign-in only lasts 24 hours).
/// </summary>
/// <param name="DeviceName">Shown nowhere yet; helps recognise the session in the database ("Chrome on Windows").</param>
public sealed record CreateSessionRequest(string? DeviceName);

/// <summary>A device session token, from POST /api/sessions or POST /api/sessions/rotate.</summary>
/// <param name="Token">Secret; send as <c>Authorization: Bearer {Token}</c>. Starts with <see cref="SessionTokens.Prefix"/>.</param>
/// <param name="ExpiresAt">When the session ends if the device doesn't sync before then (each use extends it).</param>
/// <param name="RotateAfter">From then on, the device should swap the token via POST /api/sessions/rotate.</param>
public sealed record SessionResponse(string Token, DateTimeOffset ExpiresAt, DateTimeOffset RotateAfter);

public static class SessionTokens
{
    /// <summary>Every device session token starts with this, which is how the API tells it from an Entra token.</summary>
    public const string Prefix = "bms_";
}
