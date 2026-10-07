namespace Bookmarks.Shared.Api;

/// <summary>GET /api/me: who the server thinks the caller is.</summary>
/// <param name="UserId">The Entra object id; also the <c>UserId</c> on every synced row.</param>
/// <param name="DisplayName">From the token's <c>name</c> claim (collected at sign-up).</param>
/// <param name="CreatedAt">When the account first called the API.</param>
/// <param name="HasServerData">True once any library data has been synced (Phase 4); lets a new device choose merge vs. download.</param>
public sealed record MeResponse(Guid UserId, string? DisplayName, DateTimeOffset CreatedAt, bool HasServerData);
