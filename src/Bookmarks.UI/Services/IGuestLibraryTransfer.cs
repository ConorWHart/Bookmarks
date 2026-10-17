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
    /// <paramref name="userId"/>, then empties the guest library. Books the account already has (same ISBN)
    /// aren't copied twice: their reading history, notes and shelf places go onto the account's copy.
    /// </summary>
    Task<GuestLibraryMoveResult> MoveGuestLibraryAsync(Guid userId, CancellationToken ct = default);

    /// <summary>How many books (library items) the guest library holds, for wording the sign-in question.</summary>
    Task<int> CountGuestBooksAsync(CancellationToken ct = default);

    /// <summary>Deletes the guest library (only while an account is signed in).</summary>
    Task DiscardGuestLibraryAsync(CancellationToken ct = default);
}

/// <param name="Records">Records copied into the account.</param>
/// <param name="DuplicateBooks">Books the account already had (matched by ISBN), merged rather than copied.</param>
public sealed record GuestLibraryMoveResult(int Records, int DuplicateBooks);

/// <summary>For hosts with a single local database (MAUI, tests).</summary>
public sealed class NullGuestLibraryTransfer : IGuestLibraryTransfer
{
    public Task<int> CountGuestRecordsAsync(CancellationToken ct = default) => Task.FromResult(0);

    public Task<GuestLibraryMoveResult> MoveGuestLibraryAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(new GuestLibraryMoveResult(0, 0));

    public Task<int> CountGuestBooksAsync(CancellationToken ct = default) => Task.FromResult(0);

    public Task DiscardGuestLibraryAsync(CancellationToken ct = default) => Task.CompletedTask;
}
