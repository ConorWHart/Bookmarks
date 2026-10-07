namespace Bookmarks.UI.Services;

/// <summary>
/// Moves a guest library into a signed-in account's own storage. Each account has its own local
/// database, so signing in on a shared device never exposes someone else's library.
/// </summary>
public interface IGuestLibraryTransfer
{
    /// <summary>How many records sit in the guest library (0 when there's nothing to move).</summary>
    Task<int> CountGuestRecordsAsync(CancellationToken ct = default);

    /// <summary>
    /// Copies every guest record into the signed-in account's database, re-keyed to
    /// <paramref name="userId"/>, then empties the guest library. Returns the number of records moved.
    /// </summary>
    Task<int> MoveGuestLibraryAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>For hosts with a single local database (MAUI, tests).</summary>
public sealed class NullGuestLibraryTransfer : IGuestLibraryTransfer
{
    public Task<int> CountGuestRecordsAsync(CancellationToken ct = default) => Task.FromResult(0);

    public Task<int> MoveGuestLibraryAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(0);
}
