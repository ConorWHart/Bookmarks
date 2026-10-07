using Bookmarks.Api.Auth;
using Bookmarks.Api.Data;
using Bookmarks.Shared.Api;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Bookmarks.Api.Endpoints;

public static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", GetMeAsync).WithName("GetMe");
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
}
