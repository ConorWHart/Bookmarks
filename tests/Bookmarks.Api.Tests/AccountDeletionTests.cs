using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bookmarks.Shared.Api;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bookmarks.Api.Tests;

/// <summary>DELETE /api/me (Settings → Delete account) against a real SQL Server (skipped without Docker).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class AccountDeletionTests(SqlServerFixture sql) : IDisposable
{
    private static readonly Uri MeUri = new("/api/me", UriKind.Relative);
    private static readonly Uri PushUri = new("/api/sync/push", UriKind.Relative);
    private static readonly Uri SessionsUri = new("/api/sessions", UriKind.Relative);

    private readonly ApiFactory _api = new(sql.ConnectionString);

    public void Dispose() => _api.Dispose();

    [DockerFact]
    public async Task Deleting_an_account_removes_all_its_data_and_sessions_but_nobody_elses()
    {
        var aliceId = Guid.CreateVersion7();
        var bobId = Guid.CreateVersion7();
        var alice = _api.CreateClientFor(aliceId, name: "Alice");
        var bob = _api.CreateClientFor(bobId, name: "Bob");

        await SeedLibraryAsync(alice);
        await SeedLibraryAsync(bob);
        var aliceSession = await CreateSessionAsync(alice);
        var bobSession = await CreateSessionAsync(bob);

        // Alice deletes her account using her device session, as the app does.
        var response = await Bearer(aliceSession.Token).DeleteAsync(MeUri);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using (var db = sql.CreateContext(null))
        {
            Assert.Equal(0, await CountRowsAsync(db, aliceId));
            Assert.Equal(SeededRowCount, await CountRowsAsync(db, bobId));

            Assert.False(await db.DeviceSessions.AnyAsync(s => s.UserId == aliceId));
            Assert.True(await db.DeviceSessions.AnyAsync(s => s.UserId == bobId));
            Assert.False(await db.UserAccounts.AnyAsync(a => a.Id == aliceId));
            Assert.True(await db.UserAccounts.AnyAsync(a => a.Id == bobId));
        }

        // Her session stopped working; Bob's didn't.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(aliceSession.Token).GetAsync(MeUri)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Bearer(bobSession.Token).GetAsync(MeUri)).StatusCode);

        // Signing in again (same Entra user) starts an empty library.
        var again = await alice.GetFromJsonAsync<MeResponse>(MeUri);
        Assert.NotNull(again);
        Assert.False(again.HasServerData);
        var pull = await alice.GetFromJsonAsync<SyncPullResponse>(new Uri("/api/sync/pull?since=0", UriKind.Relative));
        Assert.NotNull(pull);
        Assert.Empty(pull.Changes);
    }

    [DockerFact]
    public async Task Deleting_an_account_with_no_data_succeeds()
    {
        var client = _api.CreateClientFor(Guid.CreateVersion7());

        var response = await client.DeleteAsync(MeUri);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_requires_a_signed_in_user()
    {
        using var api = new ApiFactory();

        var response = await api.CreateClient().DeleteAsync(MeUri);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Helpers ----------

    /// <summary>Rows <see cref="SeedLibraryAsync"/> creates: one of each synced type, plus a tombstone.</summary>
    private const int SeededRowCount = 10;

    /// <summary>One record in every synced table, plus a deleted note (tombstones must go too). Also creates the account row.</summary>
    private static async Task SeedLibraryAsync(HttpClient client)
    {
        (await client.GetAsync(MeUri)).EnsureSuccessStatusCode();

        var book = new Book { Title = "Dune", Authors = ["Frank Herbert"] };
        var item = new LibraryItem { BookId = book.Id };
        var bookcase = new Bookcase { Name = "Hall" };
        var shelf = new Shelf { BookcaseId = bookcase.Id };
        var tombstone = new Note { LibraryItemId = item.Id, Body = "Deleted", IsDeleted = true };

        var changes = new[]
        {
            Change(book),
            Change(item),
            Change(new ReadingSession { LibraryItemId = item.Id }),
            Change(new Note { LibraryItemId = item.Id, Body = "Loved it" }),
            Change(tombstone),
            Change(new Tag { Name = "sci-fi" }),
            Change(bookcase),
            Change(shelf),
            Change(new Preset { Name = "Unread", Json = "{}" }),
            Change(new ShelfPlacement { ShelfId = shelf.Id, LibraryItemId = item.Id }),
        };
        Assert.Equal(SeededRowCount, changes.Length);

        var response = await client.PostAsJsonAsync(PushUri, new SyncPushRequest(Guid.CreateVersion7(), changes));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SyncPushResponse>();
        Assert.NotNull(body);
        Assert.All(body.Results, r => Assert.True(r.Accepted));
    }

    private static async Task<int> CountRowsAsync(Bookmarks.Api.Data.BookmarksDbContext db, Guid userId)
    {
        return await Count(db.Books) + await Count(db.LibraryItems) + await Count(db.ReadingSessions) + await Count(db.Notes)
            + await Count(db.Tags) + await Count(db.Bookcases) + await Count(db.Shelves) + await Count(db.Presets)
            + await Count(db.ShelfPlacements);

        // Ignore both filters: a context with no user sees nothing, and tombstones count.
        Task<int> Count<T>(IQueryable<T> rows)
            where T : SyncEntity
            => rows.IgnoreQueryFilters().CountAsync(x => x.UserId == userId);
    }

    private static SyncChange Change<T>(T entity)
        where T : SyncEntity
    {
        var type = SyncEntityTypes.Of<T>();
        return new SyncChange(type.Name, entity.Id, 0, entity.UpdatedAt, entity.IsDeleted, type.ToPayload(entity));
    }

    private static async Task<SessionResponse> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(SessionsUri, new CreateSessionRequest("Test browser"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionResponse>() ?? throw new InvalidOperationException("No session returned.");
    }

    /// <summary>A client that sends only the device session token (no test user headers).</summary>
    private HttpClient Bearer(string token)
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
