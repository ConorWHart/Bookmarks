namespace Bookmarks.Api.Data;

/// <summary>
/// A signed-in device: lets it call the API for up to 90 days without signing in to Entra again.
/// Server-only (not synced), and not covered by the owner query filter: sessions are looked up by token
/// hash before anyone is authenticated.
/// </summary>
/// <remarks>Only SHA-256 hashes of tokens are stored. The token itself is 32 random bytes, so a fast hash is enough.</remarks>
public sealed class DeviceSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The Entra object id of the user who signed in.</summary>
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the current token.</summary>
    public byte[] TokenHash { get; set; } = [];

    /// <summary>SHA-256 of the token before the last rotation; accepted until <see cref="PreviousValidUntil"/>.</summary>
    public byte[]? PreviousTokenHash { get; set; }

    public DateTimeOffset? PreviousValidUntil { get; set; }

    /// <summary>From the device ("Chrome on Windows"), for recognising sessions later.</summary>
    public string? DeviceName { get; set; }

    /// <summary>The user's display name at sign-in (the <c>name</c> claim), so requests on this session still carry it.</summary>
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the current token was issued; rotation is due a week later.</summary>
    public DateTimeOffset TokenIssuedAt { get; set; }

    /// <summary>Last request on this session (updated at most once an hour).</summary>
    public DateTimeOffset LastUsedAt { get; set; }

    /// <summary>The session ends here unless it's used before then: each use moves it to last use + 90 days.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the device signs out.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
