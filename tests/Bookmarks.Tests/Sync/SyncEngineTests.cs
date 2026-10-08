using System.Globalization;
using Bookmarks.Core.Sync;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Tests.Sync;

/// <summary>The Phase 4 sync scenarios (claude/phase4-plan.md) with two in-memory devices and a fake server.</summary>
public class SyncEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSyncServer _server = new();

    // 1. New device, empty → pulls everything.
    [Fact]
    public async Task A_new_device_downloads_the_whole_library()
    {
        var a = new Device(_server);
        var bookcase = await a.AddAsync(new Bookcase { Name = "Living room" });
        var book = await a.AddAsync(new Book { Title = "Dune", Authors = ["Frank Herbert"] });
        var item = await a.AddAsync(new LibraryItem { BookId = book.Id, Rating = 4.5m });
        await a.AddAsync(new Note { LibraryItemId = item.Id, Body = "Reread every few years" });

        var pushed = await a.SyncAsync();
        Assert.Equal(4, pushed.Pushed);
        Assert.Equal(0, await a.Store.CountOutboxAsync());

        var b = new Device(_server);
        var result = await b.SyncAsync();

        Assert.Equal(4, result.Pulled);
        Assert.True(result.LocalDataChanged);
        Assert.Equal("Living room", (await b.GetAsync<Bookcase>(bookcase.Id)).Name);
        Assert.Equal(new[] { "Frank Herbert" }, (await b.GetAsync<Book>(book.Id)).Authors);
        Assert.Equal(4.5m, (await b.GetAsync<LibraryItem>(item.Id)).Rating);
        Assert.Single(await b.Store.GetAllAsync<Note>());
        Assert.Equal(0, await b.Store.CountOutboxAsync()); // Pulled rows aren't local edits.
    }

    // 2. Existing local library + empty account → uploads everything; a second device then sees it.
    [Fact]
    public async Task The_first_sync_uploads_a_library_created_before_sync_existed()
    {
        var a = new Device(_server);
        var shelf = new Shelf { Name = "Top", WidthMm = 800 };
        var tag = new Tag { Name = "Favourites" };
        a.Store.Seed(shelf); // Version 0 and nothing in the outbox, like a library moved in during Phase 3.
        a.Store.Seed(tag);

        var first = await a.SyncAsync();
        Assert.Equal(2, first.Pushed);
        Assert.True((await a.GetAsync<Shelf>(shelf.Id)).Version > 0);

        var second = await a.SyncAsync();
        Assert.Equal(0, second.Pushed); // Only the first pass sweeps up unsynced rows.

        var b = new Device(_server);
        await b.SyncAsync();
        Assert.Equal("Top", (await b.GetAsync<Shelf>(shelf.Id)).Name);
        Assert.Equal("Favourites", (await b.GetAsync<Tag>(tag.Id)).Name);
    }

    // 3. Edit on A, sync, then sync B → B gets it.
    [Fact]
    public async Task An_edit_reaches_the_other_device()
    {
        var (a, b, bookcase) = await TwoDevicesSharingAsync(new Bookcase { Name = "Hall" });

        await a.EditAsync<Bookcase>(bookcase.Id, x => x.Name = "Study");
        await a.SyncAsync();
        var result = await b.SyncAsync();

        Assert.True(result.LocalDataChanged);
        Assert.Equal("Study", (await b.GetAsync<Bookcase>(bookcase.Id)).Name);

        // Nothing new: B's next pass changes nothing.
        Assert.False((await b.SyncAsync()).LocalDataChanged);
    }

    // 4. Offline edits on A → reconnect → pushed.
    [Fact]
    public async Task Offline_edits_stay_queued_and_are_pushed_on_reconnect()
    {
        var (a, b, tag) = await TwoDevicesSharingAsync(new Tag { Name = "sci-fi" });

        _server.Offline = true;
        await a.EditAsync<Tag>(tag.Id, x => x.Name = "Science fiction");
        var added = await a.AddAsync(new Tag { Name = "Fantasy" });
        await Assert.ThrowsAsync<HttpRequestException>(a.SyncAsync);
        Assert.Equal(2, await a.Store.CountOutboxAsync());

        _server.Offline = false;
        var result = await a.SyncAsync();
        Assert.Equal(2, result.Pushed);
        Assert.Equal(0, await a.Store.CountOutboxAsync());

        await b.SyncAsync();
        Assert.Equal("Science fiction", (await b.GetAsync<Tag>(tag.Id)).Name);
        Assert.Equal("Fantasy", (await b.GetAsync<Tag>(added.Id)).Name);
    }

    // 5. Same record edited on A and B before either syncs → the later UpdatedAt wins on both, whoever syncs first.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_edits_converge_on_the_later_one(bool earlierSyncsFirst)
    {
        var (a, b, bookcase) = await TwoDevicesSharingAsync(new Bookcase { Name = "Hall" });

        await a.EditAsync<Bookcase>(bookcase.Id, x => x.Name = "Earlier", T0.AddMinutes(1));
        await b.EditAsync<Bookcase>(bookcase.Id, x => x.Name = "Later", T0.AddMinutes(2));

        var (first, second) = earlierSyncsFirst ? (a, b) : (b, a);
        await first.SyncAsync();
        await second.SyncAsync();
        await first.SyncAsync();

        Assert.Equal("Later", (await a.GetAsync<Bookcase>(bookcase.Id)).Name);
        Assert.Equal("Later", (await b.GetAsync<Bookcase>(bookcase.Id)).Name);
        Assert.Equal("Later", _server.Get<Bookcase>(bookcase.Id).Name);
        Assert.Equal(0, await a.Store.CountOutboxAsync());
        Assert.Equal(0, await b.Store.CountOutboxAsync());
    }

    // 6. Delete on A, edit on B before syncing → last writer wins (a delete is a write).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_against_edit_is_last_writer_wins(bool deleteIsLater)
    {
        var (a, b, item) = await TwoDevicesSharingAsync(new LibraryItem { BookId = Guid.CreateVersion7(), Rating = 3m });

        await a.EditAsync<LibraryItem>(item.Id, x => x.IsDeleted = true, T0.AddMinutes(deleteIsLater ? 2 : 1));
        await b.EditAsync<LibraryItem>(item.Id, x => x.Rating = 5m, T0.AddMinutes(deleteIsLater ? 1 : 2));

        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        var onA = await a.GetAsync<LibraryItem>(item.Id);
        var onB = await b.GetAsync<LibraryItem>(item.Id);
        Assert.Equal(deleteIsLater, onA.IsDeleted);
        Assert.Equal(deleteIsLater, onB.IsDeleted);
        Assert.Equal(onA.Rating, onB.Rating);
    }

    // 7. Undo after delete syncs as a restore.
    [Fact]
    public async Task Undo_after_delete_syncs_as_a_restore()
    {
        var (a, b, item) = await TwoDevicesSharingAsync(new LibraryItem { BookId = Guid.CreateVersion7() });

        await a.EditAsync<LibraryItem>(item.Id, x => x.IsDeleted = true);
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.True((await b.GetAsync<LibraryItem>(item.Id)).IsDeleted);
        Assert.Empty(await b.Store.GetAllAsync<LibraryItem>());

        await a.EditAsync<LibraryItem>(item.Id, x => x.IsDeleted = false);
        await a.SyncAsync();
        await b.SyncAsync();
        Assert.False((await b.GetAsync<LibraryItem>(item.Id)).IsDeleted);
        Assert.Single(await b.Store.GetAllAsync<LibraryItem>());
    }

    // 8. Pull paging, and checkpoint resume after an interrupted sync.
    [Fact]
    public async Task Pull_pages_and_resumes_from_the_last_saved_checkpoint()
    {
        var a = new Device(_server);
        var ids = new List<Guid>();
        for (var i = 0; i < 25; i++)
        {
            ids.Add((await a.AddAsync(new Tag { Name = $"Tag {i}" })).Id);
        }

        await a.SyncAsync();

        var b = new Device(_server, pullPageSize: 10);
        _server.PullSinceValues.Clear();
        _server.FailPullCall = 2; // The connection drops after the first page.
        await Assert.ThrowsAsync<HttpRequestException>(b.SyncAsync);

        var saved = long.Parse((await b.Store.GetMetaAsync(SyncEngine.CheckpointKey))!, CultureInfo.InvariantCulture);
        Assert.Equal(10, (await b.Store.GetAllAsync<Tag>()).Count);

        var result = await b.SyncAsync();

        Assert.Equal(15, result.Pulled);
        Assert.Equal(ids.Order(), (await b.Store.GetAllAsync<Tag>()).Select(t => t.Id).Order());
        // First pass: page 1 from 0, then the failed call. Second pass resumes from the saved checkpoint.
        Assert.Equal(new[] { 0L, saved, saved }, _server.PullSinceValues.Take(3).ToArray());
        Assert.Equal(4, _server.PullSinceValues.Count); // 0, failed, resume, last page
    }

    // An edit made while a push is in flight stays queued for the next pass.
    [Fact]
    public async Task An_edit_during_a_push_is_not_lost()
    {
        var (a, _, bookcase) = await TwoDevicesSharingAsync(new Bookcase { Name = "Hall" });

        await a.EditAsync<Bookcase>(bookcase.Id, x => x.Name = "Study");
        _server.DuringPush = async () =>
        {
            _server.DuringPush = null;
            await a.EditAsync<Bookcase>(bookcase.Id, x => x.Name = "Library");
        };

        await a.SyncAsync();
        Assert.Equal("Study", _server.Get<Bookcase>(bookcase.Id).Name);
        Assert.Equal(1, await a.Store.CountOutboxAsync());
        Assert.Equal("Library", (await a.GetAsync<Bookcase>(bookcase.Id)).Name);

        var next = await a.SyncAsync();
        Assert.Equal(1, next.Pushed);
        Assert.Equal(0, next.Rejected);
        Assert.Equal("Library", _server.Get<Bookcase>(bookcase.Id).Name);
    }

    // A pulled record meets a newer pending local edit: the edit stays queued and is then accepted.
    [Fact]
    public async Task A_newer_pending_edit_survives_a_pull_and_wins()
    {
        var (a, b, shelf) = await TwoDevicesSharingAsync(new Shelf { Name = "Top" });

        await b.EditAsync<Shelf>(shelf.Id, x => x.Name = "From B", T0.AddMinutes(1));
        await b.SyncAsync();

        // A edits while its pull is in flight (after its push), with a later timestamp than B's.
        _server.BeforePull = async () =>
        {
            _server.BeforePull = null;
            await a.EditAsync<Shelf>(shelf.Id, x => x.Name = "From A", T0.AddMinutes(2));
        };
        var pass = await a.SyncAsync();

        Assert.False(pass.LocalDataChanged);
        Assert.Equal("From A", (await a.GetAsync<Shelf>(shelf.Id)).Name);
        Assert.Equal(1, await a.Store.CountOutboxAsync());

        var next = await a.SyncAsync();
        Assert.Equal(1, next.Pushed);
        Assert.Equal(0, next.Rejected);

        await b.SyncAsync();
        Assert.Equal("From A", (await b.GetAsync<Shelf>(shelf.Id)).Name);
    }

    // A pulled record meets an older pending local edit: the server's copy wins and the edit is dropped.
    [Fact]
    public async Task An_older_pending_edit_gives_way_to_a_newer_pulled_record()
    {
        var (a, b, shelf) = await TwoDevicesSharingAsync(new Shelf { Name = "Top" });

        await b.EditAsync<Shelf>(shelf.Id, x => x.Name = "From B", T0.AddMinutes(2));
        await b.SyncAsync();

        _server.BeforePull = async () =>
        {
            _server.BeforePull = null;
            await a.EditAsync<Shelf>(shelf.Id, x => x.Name = "From A", T0.AddMinutes(1));
        };
        var pass = await a.SyncAsync();

        Assert.True(pass.LocalDataChanged);
        Assert.Equal("From B", (await a.GetAsync<Shelf>(shelf.Id)).Name);
        Assert.Equal(0, await a.Store.CountOutboxAsync());
    }

    [Fact]
    public async Task A_completed_pass_records_the_time()
    {
        var a = new Device(_server);
        Assert.Null(await a.Engine.GetLastSyncedAtAsync());

        await a.SyncAsync();

        var at = await a.Engine.GetLastSyncedAtAsync();
        Assert.NotNull(at);
        Assert.True(DateTimeOffset.UtcNow - at.Value < TimeSpan.FromMinutes(1));
    }

    /// <summary>Device A creates the record and syncs; device B downloads it. Timestamps start at <see cref="T0"/>.</summary>
    private async Task<(Device A, Device B, T Entity)> TwoDevicesSharingAsync<T>(T entity)
        where T : SyncEntity
    {
        entity.UpdatedAt = T0;
        var a = new Device(_server);
        await a.AddAsync(entity);
        await a.SyncAsync();

        var b = new Device(_server);
        await b.SyncAsync();
        Assert.NotNull(await b.Store.GetAsync<T>(entity.Id));
        return (a, b, entity);
    }
}
