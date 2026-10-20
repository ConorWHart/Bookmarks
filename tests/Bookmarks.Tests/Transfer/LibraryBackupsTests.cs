using Bookmarks.Core.Storage;
using Bookmarks.Core.Transfer;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests.Transfer;

public class LibraryBackupsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Fact]
    public async Task Backup_holds_live_records_only_and_round_trips_through_json()
    {
        var (store, library) = await SeedAsync();
        var deleted = new LibraryItem { UserId = Owner, BookId = library.Book.Id, IsDeleted = true };
        await store.UpsertAsync(deleted);

        var json = LibraryBackups.ToJson(await LibraryBackups.CreateAsync(store, Owner, Now, "1.0.0"));
        var backup = LibraryBackups.Parse(json);

        Assert.Contains("\"format\":\"bookmarks-backup\"", json, StringComparison.Ordinal);
        Assert.Equal(Owner, backup.OwnerId);
        Assert.Equal(Now, backup.ExportedAt);
        Assert.Single(backup.Books!);
        Assert.Equal(library.Item.Id, Assert.Single(backup.LibraryItems!).Id);
        Assert.Single(backup.ReadingSessions!);
        Assert.Single(backup.Notes!);
        Assert.Single(backup.Bookcases!);
        Assert.Single(backup.Shelves!);
        Assert.Single(backup.Placements!);

        var summary = LibraryBackups.Summarize(backup, Owner);
        Assert.Equal(1, summary.Books);
        Assert.True(summary.SameOwner);
        Assert.False(LibraryBackups.Summarize(backup, Guid.CreateVersion7()).SameOwner);
    }

    [Theory]
    [InlineData("{\"name\":\"not a backup\"}")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void Other_files_are_refused(string json)
        => Assert.Throws<BackupFormatException>(() => LibraryBackups.Parse(json));

    [Fact]
    public void A_backup_from_a_newer_version_is_refused()
        => Assert.Throws<BackupFormatException>(() => LibraryBackups.Parse("{\"format\":\"bookmarks-backup\",\"schemaVersion\":99}"));

    [Fact]
    public async Task Restoring_into_an_empty_library_brings_everything_back_with_the_same_ids()
    {
        var (source, library) = await SeedAsync();
        var json = LibraryBackups.ToJson(await LibraryBackups.CreateAsync(source, Owner, Now, null));

        var target = new InMemoryLocalStore();
        var result = await LibraryBackups.RestoreAsync(target, LibraryBackups.Parse(json), RestoreMode.Merge, Owner, Now);

        Assert.Equal(new RestoreResult(1, 0, 0, 0), result);
        var item = Assert.Single(await target.GetAllAsync<LibraryItem>());
        Assert.Equal(library.Item.Id, item.Id);
        Assert.Equal(0, item.Version);
        Assert.Equal(T0, item.UpdatedAt); // Merge keeps the backup's timestamps
        Assert.Equal(library.Item.Id, Assert.Single(await target.GetAllAsync<ShelfPlacement>()).LibraryItemId);
        Assert.Equal(7, await target.CountOutboxAsync()); // book, item, session, note, bookcase, shelf, placement: all queued for sync
    }

    [Fact]
    public async Task Restoring_twice_into_the_same_library_duplicates_nothing()
    {
        var (store, _) = await SeedAsync();
        var json = LibraryBackups.ToJson(await LibraryBackups.CreateAsync(store, Owner, Now, null));

        var result = await LibraryBackups.RestoreAsync(store, LibraryBackups.Parse(json), RestoreMode.Merge, Owner, Now);

        Assert.Equal(new RestoreResult(0, 0, 1, 0), result);
        Assert.Single(await store.GetAllAsync<LibraryItem>());
        Assert.Single(await store.GetAllAsync<Note>());
    }

    [Fact]
    public async Task Merge_keeps_the_newer_copy_of_each_record()
    {
        var (store, library) = await SeedAsync();
        var json = LibraryBackups.ToJson(await LibraryBackups.CreateAsync(store, Owner, Now, null));

        // Changed here after the backup was made: a newer rating, and a note tombstone that is older than the backup's copy.
        library.Item.Rating = 2m;
        library.Item.UpdatedAt = T0.AddDays(1);
        await store.UpsertAsync(library.Item);
        library.Note.IsDeleted = true;
        library.Note.UpdatedAt = T0.AddDays(-1);
        await store.UpsertAsync(library.Note);

        var result = await LibraryBackups.RestoreAsync(store, LibraryBackups.Parse(json), RestoreMode.Merge, Owner, Now);

        Assert.Equal(1, result.Kept);
        Assert.Equal(2m, (await store.GetAsync<LibraryItem>(library.Item.Id))!.Rating);
        Assert.False((await store.GetAsync<Note>(library.Note.Id))!.IsDeleted); // the backup's copy was newer
    }

    [Fact]
    public async Task Replace_removes_what_isnt_in_the_backup_and_wins_everywhere()
    {
        var (store, library) = await SeedAsync();
        var json = LibraryBackups.ToJson(await LibraryBackups.CreateAsync(store, Owner, Now, null));

        var later = new Book { UserId = Owner, Title = "Added after the backup", UpdatedAt = T0 };
        var laterItem = new LibraryItem { UserId = Owner, BookId = later.Id, UpdatedAt = T0 };
        await store.UpsertAsync(later);
        await store.UpsertAsync(laterItem);
        library.Item.Rating = 1m;
        library.Item.UpdatedAt = T0.AddDays(2);
        await store.UpsertAsync(library.Item);

        var result = await LibraryBackups.RestoreAsync(store, LibraryBackups.Parse(json), RestoreMode.Replace, Owner, Now);

        Assert.Equal(new RestoreResult(0, 1, 0, 1), result);
        Assert.True((await store.GetAsync<LibraryItem>(laterItem.Id))!.IsDeleted);
        var restored = (await store.GetAsync<LibraryItem>(library.Item.Id))!;
        Assert.Equal(4.5m, restored.Rating);
        Assert.Equal(Now, restored.UpdatedAt); // newest, so it beats other devices' copies when it syncs
    }

    [Fact]
    public async Task A_backup_from_another_owner_gets_new_ids_with_references_intact()
    {
        var (source, library) = await SeedAsync();
        var json = LibraryBackups.ToJson(await LibraryBackups.CreateAsync(source, Owner, Now, null));
        var someoneElse = Guid.CreateVersion7();

        var target = new InMemoryLocalStore();
        await LibraryBackups.RestoreAsync(target, LibraryBackups.Parse(json), RestoreMode.Merge, someoneElse, Now);

        var book = Assert.Single(await target.GetBooksAsync());
        var item = Assert.Single(await target.GetAllAsync<LibraryItem>());
        var shelf = Assert.Single(await target.GetAllAsync<Shelf>());
        var placement = Assert.Single(await target.GetAllAsync<ShelfPlacement>());

        Assert.NotEqual(library.Item.Id, item.Id);
        Assert.Equal(book.Id, item.BookId);
        Assert.Equal(item.Id, Assert.Single(await target.GetAllAsync<ReadingSession>()).LibraryItemId);
        Assert.Equal(item.Id, Assert.Single(await target.GetAllAsync<Note>()).LibraryItemId);
        Assert.Equal(Assert.Single(await target.GetAllAsync<Bookcase>()).Id, shelf.BookcaseId);
        Assert.Equal(shelf.Id, placement.ShelfId);
        Assert.Equal(item.Id, placement.LibraryItemId);
        Assert.Equal(someoneElse, item.UserId);
    }

    private sealed record Seeded(Book Book, LibraryItem Item, Note Note);

    /// <summary>One book with a copy, a read, a note, and a place on a shelf; all dated <see cref="T0"/>.</summary>
    private static async Task<(InMemoryLocalStore Store, Seeded Library)> SeedAsync()
    {
        var store = new InMemoryLocalStore();
        var book = new Book { UserId = Owner, Title = "Dune", Authors = ["Frank Herbert"], UpdatedAt = T0 };
        var item = new LibraryItem { UserId = Owner, BookId = book.Id, Rating = 4.5m, UpdatedAt = T0 };
        var note = new Note { UserId = Owner, LibraryItemId = item.Id, Body = "Spice", UpdatedAt = T0 };
        var bookcase = new Bookcase { UserId = Owner, Name = "Hall", UpdatedAt = T0 };
        var shelf = new Shelf { UserId = Owner, BookcaseId = bookcase.Id, UpdatedAt = T0 };

        await store.UpsertAsync(book);
        await store.UpsertAsync(item);
        await store.UpsertAsync(new ReadingSession { UserId = Owner, LibraryItemId = item.Id, Outcome = ReadingOutcome.Finished, UpdatedAt = T0 });
        await store.UpsertAsync(note);
        await store.UpsertAsync(bookcase);
        await store.UpsertAsync(shelf);
        await store.UpsertAsync(new ShelfPlacement { UserId = Owner, ShelfId = shelf.Id, LibraryItemId = item.Id, UpdatedAt = T0 });
        return (store, new Seeded(book, item, note));
    }
}
