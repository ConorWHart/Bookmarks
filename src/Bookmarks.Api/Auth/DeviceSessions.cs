using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Bookmarks.Api.Data;
using Bookmarks.Shared.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bookmarks.Api.Auth;

/// <summary>Timings for device sessions (see <c>claude/device-sessions-plan.md</c>).</summary>
public static class DeviceSessionDefaults
{
    /// <summary>A session ends after this long without use; the device then has to sign in again to sync.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary>Devices swap their token for a new one once it's this old, so a copied token soon stops working.</summary>
    public static readonly TimeSpan RotateAfter = TimeSpan.FromDays(7);

    /// <summary>The old token keeps working this long after a rotation (calls already in flight, other tabs).</summary>
    public static readonly TimeSpan RotationGrace = TimeSpan.FromMinutes(5);

    /// <summary>LastUsedAt/ExpiresAt are written at most this often, so most requests don't write to the database.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromHours(1);

    /// <summary>Ended (expired or signed-out) sessions are deleted after this long.</summary>
    public static readonly TimeSpan KeepEndedFor = TimeSpan.FromDays(30);

    public const int MaxDeviceNameLength = 100;
}

/// <summary>Creating and reading device session tokens.</summary>
public static class DeviceSessionTokens
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>A new secret: <c>bms_</c> + 32 random bytes as base64url (43 characters).</summary>
    public static string NewToken() => SessionTokens.Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>The device session token in the Authorization header, or null when there's none (e.g. an Entra token).</summary>
    public static string? FromHeader(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[BearerPrefix.Length..].Trim();
        return token.StartsWith(SessionTokens.Prefix, StringComparison.Ordinal) && token.Length <= 100 ? token : null;
    }
}

/// <summary>A valid session found for a token.</summary>
/// <param name="IsPreviousToken">The token was the one before the last rotation (still inside its grace window).</param>
public sealed record DeviceSessionMatch(Guid SessionId, Guid UserId, string? DisplayName, bool IsPreviousToken);

/// <summary>
/// Device sessions in the database. Uses its own context with no current user: sessions are read before the request
/// is authenticated, and a context built then would hide every user's rows for the rest of the request.
/// </summary>
public sealed class DeviceSessionService(DbContextOptions<BookmarksDbContext> dbOptions, TimeProvider time)
{
    /// <summary>Starts a session for a user who just proved who they are with an Entra token.</summary>
    public async Task<SessionResponse> CreateAsync(Guid userId, string? displayName, string? deviceName, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var token = DeviceSessionTokens.NewToken();

        await using var db = CreateContext();

        // Housekeeping: drop sessions that ended a while ago (any user's; there are only a handful).
        var cutoff = now - DeviceSessionDefaults.KeepEndedFor;
        await db.DeviceSessions
            .Where(s => s.ExpiresAt < cutoff || (s.RevokedAt != null && s.RevokedAt < cutoff))
            .ExecuteDeleteAsync(ct);

        db.DeviceSessions.Add(new DeviceSession
        {
            UserId = userId,
            TokenHash = DeviceSessionTokens.Hash(token),
            DeviceName = Clip(deviceName, DeviceSessionDefaults.MaxDeviceNameLength),
            DisplayName = Clip(displayName, 200),
            CreatedAt = now,
            TokenIssuedAt = now,
            LastUsedAt = now,
            ExpiresAt = now + DeviceSessionDefaults.Lifetime,
        });
        await db.SaveChangesAsync(ct);

        return new SessionResponse(token, now + DeviceSessionDefaults.Lifetime, now + DeviceSessionDefaults.RotateAfter);
    }

    /// <summary>The live session for a token, or null if it's unknown, expired, signed out or a rotated-out token past its grace.</summary>
    public async Task<DeviceSessionMatch?> ValidateAsync(string token, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = DeviceSessionTokens.Hash(token);

        await using var db = CreateContext();
        var session = await db.DeviceSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TokenHash == hash || s.PreviousTokenHash == hash, ct);

        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
        {
            return null;
        }

        var isPrevious = !session.TokenHash.AsSpan().SequenceEqual(hash);
        if (isPrevious && !(session.PreviousValidUntil > now))
        {
            return null;
        }

        // Sliding expiry: each use (at most once an hour) moves the end to 90 days from now.
        if (now - session.LastUsedAt >= DeviceSessionDefaults.TouchInterval)
        {
            var expires = now + DeviceSessionDefaults.Lifetime;
            await db.DeviceSessions
                .Where(s => s.Id == session.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastUsedAt, now).SetProperty(s => s.ExpiresAt, expires), ct);
        }

        return new DeviceSessionMatch(session.Id, session.UserId, session.DisplayName, isPrevious);
    }

    /// <summary>
    /// Replaces the session's token. Only the current token can rotate; returns null if it isn't current any more
    /// (another tab rotated first), and the caller should re-read its stored token.
    /// </summary>
    public async Task<SessionResponse?> RotateAsync(Guid sessionId, string currentToken, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var oldHash = DeviceSessionTokens.Hash(currentToken);
        var token = DeviceSessionTokens.NewToken();
        var newHash = DeviceSessionTokens.Hash(token);
        var graceUntil = now + DeviceSessionDefaults.RotationGrace;
        var expires = now + DeviceSessionDefaults.Lifetime;

        await using var db = CreateContext();

        // One statement, conditional on the old token still being current, so two rotations can't both win.
        var updated = await db.DeviceSessions
            .Where(s => s.Id == sessionId && s.TokenHash == oldHash && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.PreviousTokenHash, oldHash)
                .SetProperty(s => s.PreviousValidUntil, graceUntil)
                .SetProperty(s => s.TokenHash, newHash)
                .SetProperty(s => s.TokenIssuedAt, now)
                .SetProperty(s => s.LastUsedAt, now)
                .SetProperty(s => s.ExpiresAt, expires), ct);

        return updated == 1 ? new SessionResponse(token, expires, now + DeviceSessionDefaults.RotateAfter) : null;
    }

    /// <summary>Signs the device out: the session's tokens stop working straight away.</summary>
    public async Task RevokeAsync(Guid sessionId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var db = CreateContext();
        await db.DeviceSessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now), ct);
    }

    private BookmarksDbContext CreateContext() => new(dbOptions, NoCurrentUser.Instance);

    private static string? Clip(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

/// <summary>
/// Authenticates <c>Authorization: Bearer bms_…</c>. The principal carries the same claims as an Entra token
/// (oid, scp, name), so policies, <see cref="ICurrentUser"/> and the query filters work unchanged, plus
/// <see cref="AuthConstants.SessionIdClaim"/>.
/// </summary>
internal sealed class DeviceSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DeviceSessionService sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (DeviceSessionTokens.FromHeader(Request) is not { } token)
        {
            return AuthenticateResult.NoResult();
        }

        if (await sessions.ValidateAsync(token, Context.RequestAborted) is not { } match)
        {
            return AuthenticateResult.Fail("The device session is unknown, expired or signed out.");
        }

        var claims = new List<Claim>
        {
            new(AuthConstants.ObjectIdClaim, match.UserId.ToString()),
            new(AuthConstants.ScopeClaim, AuthConstants.ApiScope),
            new(AuthConstants.SessionIdClaim, match.SessionId.ToString()),
        };

        if (match.DisplayName is { } name)
        {
            claims.Add(new Claim(AuthConstants.NameClaim, name));
        }

        if (match.IsPreviousToken)
        {
            claims.Add(new Claim(AuthConstants.PreviousSessionTokenClaim, "true"));
        }

        var identity = new ClaimsIdentity(claims, AuthConstants.DeviceSessionScheme, AuthConstants.NameClaim, roleType: null);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), AuthConstants.DeviceSessionScheme));
    }
}
