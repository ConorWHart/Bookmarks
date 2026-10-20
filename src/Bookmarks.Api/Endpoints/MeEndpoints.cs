using Bookmarks.Api.Auth;
using Bookmarks.Api.Data;
using Bookmarks.Shared.Api;
using Bookmarks.Shared.Sync;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bookmarks.Api.Endpoints;

public static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", GetMeAsync).WithName("GetMe");
        api.MapDelete("/me", DeleteMeAsync).WithName("DeleteMe");
        return api;
    }

    /// <summary>
    /// Returns the caller's account, creating the <see cref="UserAccount"/> row on first call.
    /// The client calls this right after sign-in to confirm the token works end to end.
    /// </summary>
    internal static async Task<Ok<MeResponse>> GetMeAsync(
        ICurrentUser user,
        BookmarksDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var userId = user.RequiredId();
        var now = time.GetUtcNow();

        var account = await db.UserAccounts.FindAsync([userId], ct);
        if (account is null)
        {
            account = new UserAccount { Id = userId, CreatedAt = now };
            db.UserAccounts.Add(account);
        }

        account.LastSeenAt = now;
        account.DisplayName = user.DisplayName ?? account.DisplayName;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (db.Entry(account).State == EntityState.Added)
        {
            // Two first calls raced (e.g. two tabs); the other one created the row. Nothing to do.
        }

        // Query filters scope these to the caller.
        var hasServerData = await db.LibraryItems.AnyAsync(ct) || await db.Bookcases.AnyAsync(ct);

        return TypedResults.Ok(new MeResponse(userId, account.DisplayName, account.CreatedAt, hasServerData));
    }

    /// <summary>
    /// Settings → Delete account: removes every row the caller owns in every synced table (tombstones included), their
    /// device sessions and their <see cref="UserAccount"/>, in one transaction. The Entra user is not touched (decision
    /// 2026-10-19): signing in again starts an empty library. The caller's token stops working once this returns.
    /// </summary>
    internal static async Task<NoContent> DeleteMeAsync(
        ICurrentUser user,
        BookmarksDbContext db,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var userId = user.RequiredId();
        var deleted = 0;

        // The retrying execution strategy (DataSetup) only allows a transaction inside ExecuteAsync, which reruns the
        // whole block on a transient failure.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            deleted = await DeleteOwnedAsync(db.ShelfPlacements, userId, ct)
                + await DeleteOwnedAsync(db.Shelves, userId, ct)
                + await DeleteOwnedAsync(db.Bookcases, userId, ct)
                + await DeleteOwnedAsync(db.Notes, userId, ct)
                + await DeleteOwnedAsync(db.ReadingSessions, userId, ct)
                + await DeleteOwnedAsync(db.LibraryItems, userId, ct)
                + await DeleteOwnedAsync(db.Books, userId, ct)
                + await DeleteOwnedAsync(db.Tags, userId, ct)
                + await DeleteOwnedAsync(db.Presets, userId, ct);

            await db.DeviceSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserAccounts.Where(a => a.Id == userId).ExecuteDeleteAsync(ct);

            await transaction.CommitAsync(ct);
        });

        loggers.CreateLogger(typeof(MeEndpoints)).LogInformation(
            "Deleted the data of user {UserId}: {Rows} library rows.", userId, deleted);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Every row of <paramref name="userId"/>, deleted rows included. The user id is matched explicitly rather than
    /// through the owner filter, so this never depends on how the context was created.
    /// </summary>
    private static Task<int> DeleteOwnedAsync<T>(IQueryable<T> rows, Guid userId, CancellationToken ct)
        where T : SyncEntity
        => rows.IgnoreQueryFilters().Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
}
