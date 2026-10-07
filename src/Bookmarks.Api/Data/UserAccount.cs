namespace Bookmarks.Api.Data;

/// <summary>
/// One row per signed-in user, created on first call to <c>GET /api/me</c>.
/// Server-only: not synced to devices.
/// </summary>
public sealed class UserAccount
{
    /// <summary>The Entra object id (<c>oid</c>); the same value as <c>SyncEntity.UserId</c> on the user's rows.</summary>
    public Guid Id { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
