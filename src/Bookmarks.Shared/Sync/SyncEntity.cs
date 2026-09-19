namespace Bookmarks.Shared.Sync;

/// <summary>
/// Base type for every user-owned record that syncs between the device and Azure.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Id"/> is generated on the client (UUIDv7, time-ordered) so records can be created offline.</item>
/// <item><see cref="UpdatedAt"/> is set by the client on every local change and drives last-writer-wins.</item>
/// <item><see cref="Version"/> is assigned by the server (SQL rowversion) and is the pull checkpoint; 0 = never synced.</item>
/// <item><see cref="IsDeleted"/> is a tombstone so deletes propagate to other devices.</item>
/// </list>
/// </remarks>
public abstract class SyncEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public long Version { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>Call whenever the entity is modified locally.</summary>
    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
