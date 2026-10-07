using System.Net;
using System.Net.Http.Json;
using Bookmarks.Api.Data;
using Bookmarks.Shared.Api;
using Bookmarks.Shared.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bookmarks.Api.Tests;

/// <summary>Model, rowversion and per-user isolation against a real SQL Server (skipped without Docker).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class DataTests(SqlServerFixture sql)
{
    [DockerFact]
    public async Task Rowversion_is_assigned_on_insert_and_increases_on_update()
    {
        var userId = Guid.CreateVersion7();
        var bookcase = new Bookcase { UserId = userId, Name = "Living room" };

        await using (var db = sql.CreateContext(userId))
        {
            db.Bookcases.Add(bookcase);
            await db.SaveChangesAsync();
        }

        var inserted = bookcase.Version;
        Assert.True(inserted > 0);

        await using (var db = sql.CreateContext(userId))
        {
            var row = await db.Bookcases.SingleAsync(b => b.Id == bookcase.Id);
            Assert.Equal(inserted, row.Version);

            row.Name = "Study";
            row.Touch();
            await db.SaveChangesAsync();

            Assert.True(row.Version > inserted);
        }
    }

    [DockerFact]
    public async Task Queries_only_see_the_current_users_rows()
    {
        var alice = Guid.CreateVersion7();
        var bob = Guid.CreateVersion7();

        await using (var db = sql.CreateContext(null))
        {
            db.Bookcases.AddRange(
                new Bookcase { UserId = alice, Name = "Alice's" },
                new Bookcase { UserId = bob, Name = "Bob's" });
            await db.SaveChangesAsync();
        }

        await using (var db = sql.CreateContext(alice))
        {
            var names = await db.Bookcases.Select(b => b.Name).ToListAsync();
            Assert.Equal(new[] { "Alice's" }, names);
        }

        await using (var db = sql.CreateContext(null))
        {
            // No user: nothing, rather than everything.
            Assert.Empty(await db.Bookcases.Where(b => b.UserId == alice || b.UserId == bob).ToListAsync());
        }
    }

    [DockerFact]
    public async Task Tombstones_are_hidden_unless_the_NotDeleted_filter_is_ignored()
    {
        var userId = Guid.CreateVersion7();

        await using (var db = sql.CreateContext(userId))
        {
            db.Tags.AddRange(
                new Tag { UserId = userId, Name = "kept" },
                new Tag { UserId = userId, Name = "deleted", IsDeleted = true });
            await db.SaveChangesAsync();
        }

        await using (var db = sql.CreateContext(userId))
        {
            Assert.Equal(new[] { "kept" }, await db.Tags.Select(t => t.Name).ToListAsync());

            var withTombstones = await db.Tags
                .IgnoreQueryFilters([BookmarksDbContext.NotDeletedFilter])
                .OrderBy(t => t.Name)
                .Select(t => t.Name)
                .ToListAsync();
            Assert.Equal(new[] { "deleted", "kept" }, withTombstones);
        }
    }

    [DockerFact]
    public async Task LibraryItem_round_trips_complex_and_collection_columns()
    {
        var userId = Guid.CreateVersion7();
        var tag = Guid.CreateVersion7();
        var item = new LibraryItem
        {
            UserId = userId,
            BookId = Guid.CreateVersion7(),
            Status = OwnershipStatus.Wishlist,
            Format = BookFormat.MassMarketPaperback,
            Condition = BookCondition.LikeNew,
            ShelfRank = "a0V",
            AcquiredOn = new DateOnly(2024, 5, 1),
            PurchasePrice = 12.99m,
            Rating = 4.5m,
            TagIds = [tag],
            Appearance = new SpineAppearance { SpineColor = "#335577", Style = SpineStyle.Banded, FaceOut = true },
        };

        await using (var db = sql.CreateContext(userId))
        {
            db.LibraryItems.Add(item);
            await db.SaveChangesAsync();
        }

        await using (var db = sql.CreateContext(userId))
        {
            var row = await db.LibraryItems.SingleAsync(i => i.Id == item.Id);
            Assert.Equal(OwnershipStatus.Wishlist, row.Status);
            Assert.Equal(BookCondition.LikeNew, row.Condition);
            Assert.Equal(new DateOnly(2024, 5, 1), row.AcquiredOn);
            Assert.Equal(4.5m, row.Rating);
            Assert.Equal(new[] { tag }, row.TagIds);
            Assert.Equal("#335577", row.Appearance.SpineColor);
            Assert.Equal(SpineStyle.Banded, row.Appearance.Style);
            Assert.True(row.Appearance.FaceOut);
        }
    }

    [DockerFact]
    public async Task Me_creates_the_account_on_first_call_and_keeps_it()
    {
        using var api = new ApiFactory(sql.ConnectionString);
        var userId = Guid.CreateVersion7();
        var client = api.CreateClientFor(userId, name: "Conor");

        var first = await client.GetFromJsonAsync<MeResponse>(new Uri("/api/me", UriKind.Relative));
        Assert.NotNull(first);
        Assert.Equal(userId, first.UserId);
        Assert.Equal("Conor", first.DisplayName);
        Assert.False(first.HasServerData);

        var second = await client.GetFromJsonAsync<MeResponse>(new Uri("/api/me", UriKind.Relative));
        Assert.NotNull(second);
        Assert.Equal(first.CreatedAt, second.CreatedAt);

        await using var db = sql.CreateContext(userId);
        Assert.Equal(1, await db.UserAccounts.CountAsync(a => a.Id == userId));
    }

    [DockerFact]
    public async Task Me_reports_server_data_only_for_the_owner()
    {
        using var api = new ApiFactory(sql.ConnectionString);
        var owner = Guid.CreateVersion7();

        await using (var db = sql.CreateContext(owner))
        {
            db.Bookcases.Add(new Bookcase { UserId = owner, Name = "Hall" });
            await db.SaveChangesAsync();
        }

        var mine = await api.CreateClientFor(owner).GetFromJsonAsync<MeResponse>(new Uri("/api/me", UriKind.Relative));
        var theirs = await api.CreateClientFor(Guid.CreateVersion7()).GetFromJsonAsync<MeResponse>(new Uri("/api/me", UriKind.Relative));

        Assert.True(mine!.HasServerData);
        Assert.False(theirs!.HasServerData);
    }

    [DockerFact]
    public async Task Me_without_scope_does_not_touch_the_database()
    {
        using var api = new ApiFactory(sql.ConnectionString);
        var userId = Guid.CreateVersion7();

        var response = await api.CreateClientFor(userId, scopes: "openid").GetAsync(new Uri("/api/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = sql.CreateContext(userId);
        Assert.False(await db.UserAccounts.AnyAsync(a => a.Id == userId));
    }
}
