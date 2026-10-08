using System.Net;
using System.Net.Http.Json;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bookmarks.Api.Tests;

/// <summary>/api/sync/push and /api/sync/pull against a real SQL Server (skipped without Docker).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SyncTests(SqlServerFixture sql) : IDisposable
{
    private static readonly Uri PushUri = new("/api/sync/push", UriKind.Relative);

    private readonly ApiFactory _api = new(sql.ConnectionString);

    public void Dispose() => _api.Dispose();

    [DockerFact]
    public async Task Push_inserts_new_records_and_pull_returns_them()
    {
        var user = Guid.CreateVersion7();
        var client = _api.CreateClientFor(user);
        var book = new Book { Title = "Dune", Authors = ["Frank Herbert"], Genres = ["Science fiction"], PageCount = 412 };
        var item = new LibraryItem { BookId = book.Id, Rating = 4.5m, Appearance = new SpineAppearance { SpineColor = "#D9822B" } };

        var push = await PushAsync(client, Change(book), Change(item));

        Assert.All(push.Results, r => Assert.True(r.Accepted));
        Assert.All(push.Results, r => Assert.True(r.Version > 0));

        var pull = await PullAsync(client, since: 0);
        Assert.False(pull.HasMore);
        Assert.Equal(new[] { book.Id, item.Id }.Order(), pull.Changes.Select(c => c.Id).Order());
        Assert.Equal(pull.Changes.Max(c => c.BaseVersion), pull.Checkpoint);

        var pulledBook = (Book)SyncEntityTypes.Book.FromPayload(pull.Changes.Single(c => c.Id == book.Id).Payload);
        Assert.Equal("Dune", pulledBook.Title);
        Assert.Equal(new[] { "Frank Herbert" }, pulledBook.Authors);
        Assert.Equal(user, pulledBook.UserId);

        var pulledItem = (LibraryItem)SyncEntityTypes.LibraryItem.FromPayload(pull.Changes.Single(c => c.Id == item.Id).Payload);
        Assert.Equal(4.5m, pulledItem.Rating);
        Assert.Equal("#D9822B", pulledItem.Appearance.SpineColor);

        // Nothing new after the checkpoint.
        var again = await PullAsync(client, pull.Checkpoint);
        Assert.Empty(again.Changes);
        Assert.Equal(pull.Checkpoint, again.Checkpoint);
    }

    [DockerFact]
    public async Task Edit_based_on_the_current_version_is_accepted_even_with_an_older_timestamp()
    {
        var client = _api.CreateClientFor(Guid.CreateVersion7());
        var tag = new Tag { Name = "fantasy", UpdatedAt = DateTimeOffset.UtcNow };
        var v1 = (await PushAsync(client, Change(tag))).Results[0].Version;

        // The device's clock went backwards, but nobody else touched the record, so there is no conflict.
        tag.Name = "Fantasy";
        tag.UpdatedAt = tag.UpdatedAt.AddHours(-1);
        var result = (await PushAsync(client, Change(tag, baseVersion: v1))).Results[0];

        Assert.True(result.Accepted);
        Assert.True(result.Version > v1);
        Assert.Equal("Fantasy", (await ServerCopyAsync<Tag>(client, tag.Id)).Name);
    }

    [DockerFact]
    public async Task Conflicting_edits_are_decided_by_the_later_timestamp()
    {
        var client = _api.CreateClientFor(Guid.CreateVersion7());
        var start = DateTimeOffset.UtcNow.AddMinutes(-10);
        var bookcase = new Bookcase { Name = "Hall", UpdatedAt = start };
        var v1 = (await PushAsync(client, Change(bookcase))).Results[0].Version;

        // Device A and device B both edit from v1. B's edit is later, but A syncs first.
        var a = Copy(bookcase, x => (x.Name, x.UpdatedAt) = ("Study", start.AddMinutes(2)));
        var b = Copy(bookcase, x => (x.Name, x.UpdatedAt) = ("Library", start.AddMinutes(5)));

        var fromA = (await PushAsync(client, Change(a, baseVersion: v1))).Results[0];
        var fromB = (await PushAsync(client, Change(b, baseVersion: v1))).Results[0];
        Assert.True(fromA.Accepted);
        Assert.True(fromB.Accepted); // Conflict, and B is the later writer.
        Assert.Equal("Library", (await ServerCopyAsync<Bookcase>(client, bookcase.Id)).Name);

        // A stale, older edit loses and gets the server's version back.
        var stale = Copy(bookcase, x => (x.Name, x.UpdatedAt) = ("Attic", start.AddMinutes(3)));
        var fromStale = (await PushAsync(client, Change(stale, baseVersion: v1))).Results[0];
        Assert.False(fromStale.Accepted);
        Assert.Equal(fromB.Version, fromStale.Version);
        Assert.Equal("Library", (await ServerCopyAsync<Bookcase>(client, bookcase.Id)).Name);
    }

    [DockerFact]
    public async Task Delete_reaches_pull_as_a_tombstone_and_undo_restores()
    {
        var client = _api.CreateClientFor(Guid.CreateVersion7());
        var note = new Note { LibraryItemId = Guid.CreateVersion7(), Body = "Loved the ending" };
        var v1 = (await PushAsync(client, Change(note))).Results[0].Version;
        var checkpoint = (await PullAsync(client, 0)).Checkpoint;

        note.IsDeleted = true;
        note.Touch();
        var v2 = (await PushAsync(client, Change(note, baseVersion: v1))).Results[0].Version;

        var afterDelete = await PullAsync(client, checkpoint);
        var tombstone = Assert.Single(afterDelete.Changes);
        Assert.True(tombstone.IsDeleted);
        Assert.Equal(v2, tombstone.BaseVersion);

        note.IsDeleted = false;
        note.Touch();
        var restore = (await PushAsync(client, Change(note, baseVersion: v2))).Results[0];
        Assert.True(restore.Accepted);

        var afterRestore = await PullAsync(client, afterDelete.Checkpoint);
        Assert.False(Assert.Single(afterRestore.Changes).IsDeleted);
    }

    [DockerFact]
    public async Task Users_cannot_read_or_overwrite_each_others_records()
    {
        var aliceId = Guid.CreateVersion7();
        var alice = _api.CreateClientFor(aliceId);
        var bob = _api.CreateClientFor(Guid.CreateVersion7());

        var shelf = new Shelf { BookcaseId = Guid.CreateVersion7(), Name = "Alice's shelf" };
        await PushAsync(alice, Change(shelf));

        // Bob reuses Alice's id, claims to be Alice in the payload, and has a much later timestamp.
        var forged = Copy(shelf, x => (x.Name, x.UserId, x.UpdatedAt) = ("Bob was here", aliceId, DateTimeOffset.UtcNow));
        var result = (await PushAsync(bob, Change(forged, baseVersion: 0))).Results[0];

        Assert.False(result.Accepted);
        Assert.Equal(0L, result.Version);
        Assert.Equal("Alice's shelf", (await ServerCopyAsync<Shelf>(alice, shelf.Id)).Name);
        Assert.Empty((await PullAsync(bob, 0)).Changes);
    }

    [DockerFact]
    public async Task The_owner_always_comes_from_the_token()
    {
        var userId = Guid.CreateVersion7();
        var client = _api.CreateClientFor(userId);
        var preset = new Preset { Name = "Unread", UserId = Guid.CreateVersion7() }; // someone else's id in the payload

        await PushAsync(client, Change(preset));

        await using var db = sql.CreateContext(userId);
        Assert.Equal(userId, (await db.Presets.SingleAsync(p => p.Id == preset.Id)).UserId);
    }

    [DockerFact]
    public async Task Timestamps_from_the_future_are_clamped()
    {
        var client = _api.CreateClientFor(Guid.CreateVersion7());
        var session = new ReadingSession { LibraryItemId = Guid.CreateVersion7(), UpdatedAt = DateTimeOffset.UtcNow.AddDays(30) };

        await PushAsync(client, Change(session));

        var stored = Assert.Single((await PullAsync(client, 0)).Changes);
        Assert.True(stored.UpdatedAt <= DateTimeOffset.UtcNow + SyncLimits.MaxClockSkew + TimeSpan.FromMinutes(1));
    }

    [DockerFact]
    public async Task Pull_pages_in_version_order_across_tables()
    {
        var client = _api.CreateClientFor(Guid.CreateVersion7());
        var changes = new List<SyncChange>();
        for (var i = 0; i < 4; i++)
        {
            changes.Add(Change(new Bookcase { Name = $"Bookcase {i}" }));
            changes.Add(Change(new Tag { Name = $"Tag {i}" }));
        }

        await PushAsync(client, changes.Take(5).ToArray());
        await PushAsync(client, changes.Skip(5).ToArray());

        var seen = new List<SyncChange>();
        var checkpoint = 0L;
        var pages = 0;
        while (true)
        {
            var page = await PullAsync(client, checkpoint, limit: 3);
            pages++;
            Assert.True(page.Changes.Count <= 3);
            seen.AddRange(page.Changes);
            checkpoint = page.Checkpoint;
            if (!page.HasMore)
            {
                break;
            }
        }

        Assert.Equal(3, pages);
        Assert.Equal(changes.Select(c => c.Id).Order(), seen.Select(c => c.Id).Order());
        Assert.Equal(seen.Select(c => c.BaseVersion).Order(), seen.Select(c => c.BaseVersion)); // ascending
        Assert.Equal(seen.Count, seen.Select(c => c.Id).Distinct().Count());
    }

    [DockerFact]
    public async Task Invalid_pushes_are_rejected_as_a_whole()
    {
        var client = _api.CreateClientFor(Guid.CreateVersion7());
        var tag = new Tag { Name = "x" };
        var good = Change(tag);

        var unknown = await client.PostAsJsonAsync(PushUri, Request(good with { EntityType = "spaceship" }));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var duplicate = await client.PostAsJsonAsync(PushUri, Request(good, good));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var tooMany = Enumerable.Range(0, SyncLimits.MaxPushBatch + 1).Select(_ => Change(new Tag { Name = "t" })).ToArray();
        var big = await client.PostAsJsonAsync(PushUri, Request(tooMany));
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);

        // Nothing from the rejected requests was stored.
        Assert.Empty((await PullAsync(client, 0)).Changes);
    }

    [Fact]
    public async Task Sync_requires_the_api_scope()
    {
        using var api = new ApiFactory();
        var client = api.CreateClientFor(Guid.CreateVersion7(), scopes: "openid");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(new Uri("/api/sync/pull", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(PushUri, Request())).StatusCode);
    }

    // ---------- Helpers ----------

    private static SyncChange Change<T>(T entity, long baseVersion = 0)
        where T : SyncEntity
    {
        var type = SyncEntityTypes.Of<T>();
        return new SyncChange(type.Name, entity.Id, baseVersion, entity.UpdatedAt, entity.IsDeleted, type.ToPayload(entity));
    }

    private static SyncPushRequest Request(params SyncChange[] changes) => new(Guid.CreateVersion7(), changes);

    private static async Task<SyncPushResponse> PushAsync(HttpClient client, params SyncChange[] changes)
    {
        var response = await client.PostAsJsonAsync(PushUri, Request(changes));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SyncPushResponse>();
        Assert.NotNull(body);
        Assert.Equal(changes.Select(c => c.Id), body.Results.Select(r => r.Id));
        return body;
    }

    private static async Task<SyncPullResponse> PullAsync(HttpClient client, long since, int? limit = null)
    {
        var query = limit is { } l ? $"?since={since}&limit={l}" : $"?since={since}";
        var body = await client.GetFromJsonAsync<SyncPullResponse>(new Uri($"/api/sync/pull{query}", UriKind.Relative));
        Assert.NotNull(body);
        return body;
    }

    /// <summary>The record as the server has it now (via a full pull).</summary>
    private static async Task<T> ServerCopyAsync<T>(HttpClient client, Guid id)
        where T : SyncEntity
    {
        var change = (await PullAsync(client, 0)).Changes.Single(c => c.Id == id);
        return (T)SyncEntityTypes.Of<T>().FromPayload(change.Payload);
    }

    /// <summary>A second device's copy of a record (a JSON round trip), changed by <paramref name="edit"/>.</summary>
    private static T Copy<T>(T entity, Action<T> edit)
        where T : SyncEntity
    {
        var type = SyncEntityTypes.Of<T>();
        var copy = (T)type.FromPayload(type.ToPayload(entity));
        edit(copy);
        return copy;
    }
}
