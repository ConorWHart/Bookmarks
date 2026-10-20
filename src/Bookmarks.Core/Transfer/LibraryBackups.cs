using System.Globalization;
using System.Text.Json;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Backup;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Serialization;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Transfer;

/// <summary>How a backup is restored.</summary>
public enum RestoreMode
{
    /// <summary>Per record, the newer copy (<see cref="SyncEntity.UpdatedAt"/>) wins; nothing is removed.</summary>
    Merge,

    /// <summary>The library becomes the backup: everything not in the file is removed (soft-deleted, so it syncs).</summary>
    Replace,
}

/// <summary>What a backup holds, for the restore dialog.</summary>
/// <param name="SameOwner">Made by this account (or, for a signed-out library, while signed out): ids are kept.</param>
public sealed record BackupSummary(
    int Books,
    int Bookcases,
    int ReadingSessions,
    int Notes,
    DateTimeOffset ExportedAt,
    bool SameOwner);

/// <summary>Restore outcome, counted in books (library items); the other records follow their books.</summary>
/// <param name="Kept">Merge: books whose copy in the library was newer, so it stayed as it was.</param>
/// <param name="Removed">Replace: books that weren't in the backup.</param>
public sealed record RestoreResult(int Added, int Updated, int Kept, int Removed);

/// <summary>The file isn't a backup this version can read. The message is meant for the user.</summary>
public sealed class BackupFormatException : Exception
{
    public BackupFormatException()
    {
    }

    public BackupFormatException(string message)
        : base(message)
    {
    }

    public BackupFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>JSON backup and restore (Phase 7b).</summary>
/// <remarks>
/// Restored records are ordinary local edits (<see cref="ILocalStore.UpsertManyAsync{T}"/>), so they go through the
/// outbox and sync. Restoring into the account (or signed-out library) that made the backup keeps record ids, so a
/// second restore doesn't duplicate anything; into any other library every id is replaced (the server refuses ids that
/// belong to another user) and the backup's books are added alongside.
/// </remarks>
public static class LibraryBackups
{
    private static readonly BookmarksJsonContext Json = BookmarksJsonContext.Default;

    /// <summary>"bookmarks-backup-2026-10-19.json" for the given local date.</summary>
    public static string FileName(DateOnly date)
        => "bookmarks-backup-" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json";

    /// <summary>Everything live in the library: books with their copies, reading sessions, notes, bookcases, shelves, placements.</summary>
    public static async Task<LibraryBackup> CreateAsync(
        ILocalStore store,
        Guid ownerId,
        DateTimeOffset now,
        string? appVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var books = (await store.GetBooksAsync(ct)).Where(b => !b.IsDeleted).ToDictionary(b => b.Id);
        var items = (await store.GetAllAsync<LibraryItem>(ct: ct)).Where(i => books.ContainsKey(i.BookId)).ToList();
        var itemIds = items.Select(i => i.Id).ToHashSet();
        var bookcases = (await store.GetAllAsync<Bookcase>(ct: ct)).ToList();
        var bookcaseIds = bookcases.Select(b => b.Id).ToHashSet();
        var shelves = (await store.GetAllAsync<Shelf>(ct: ct)).Where(s => bookcaseIds.Contains(s.BookcaseId)).ToList();
        var shelfIds = shelves.Select(s => s.Id).ToHashSet();

        return new LibraryBackup
        {
            Format = LibraryBackup.FormatName,
            SchemaVersion = LibraryBackup.CurrentSchemaVersion,
            ExportedAt = now,
            OwnerId = ownerId,
            AppVersion = appVersion,
            Books = items.Select(i => i.BookId).Distinct().Select(id => books[id]).ToList(),
            LibraryItems = items,
            ReadingSessions = (await store.GetAllAsync<ReadingSession>(ct: ct)).Where(s => itemIds.Contains(s.LibraryItemId)).ToList(),
            Notes = (await store.GetAllAsync<Note>(ct: ct)).Where(n => itemIds.Contains(n.LibraryItemId)).ToList(),
            Bookcases = bookcases,
            Shelves = shelves,
            Placements = (await store.GetAllAsync<ShelfPlacement>(ct: ct))
                .Where(p => shelfIds.Contains(p.ShelfId) && itemIds.Contains(p.LibraryItemId))
                .ToList(),
        };
    }

    public static string ToJson(LibraryBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return JsonSerializer.Serialize(backup, Json.LibraryBackup);
    }

    /// <summary>Reads a backup file. Throws <see cref="BackupFormatException"/> with a user-facing message.</summary>
    public static LibraryBackup Parse(string json)
    {
        LibraryBackup? backup;
        try
        {
            backup = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize(json, Json.LibraryBackup);
        }
        catch (JsonException ex)
        {
            throw new BackupFormatException("This file isn't a Bookmarks backup (it couldn't be read).", ex);
        }

        if (backup is null || !string.Equals(backup.Format, LibraryBackup.FormatName, StringComparison.Ordinal))
        {
            throw new BackupFormatException("This file isn't a Bookmarks backup.");
        }

        if (backup.SchemaVersion > LibraryBackup.CurrentSchemaVersion)
        {
            throw new BackupFormatException("This backup was made by a newer version of Bookmarks. Reload the app to update it, then try again.");
        }

        backup.Books ??= [];
        backup.LibraryItems ??= [];
        backup.ReadingSessions ??= [];
        backup.Notes ??= [];
        backup.Bookcases ??= [];
        backup.Shelves ??= [];
        backup.Placements ??= [];
        return backup;
    }

    public static BackupSummary Summarize(LibraryBackup backup, Guid ownerId)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return new BackupSummary(
            backup.LibraryItems?.Count ?? 0,
            backup.Bookcases?.Count ?? 0,
            backup.ReadingSessions?.Count ?? 0,
            backup.Notes?.Count ?? 0,
            backup.ExportedAt,
            backup.OwnerId == ownerId);
    }

    /// <summary>
    /// Writes the backup into the library as <paramref name="ownerId"/>'s records. The backup's lists are modified
    /// (owner, ids, timestamps); parse the file again to restore it a second time.
    /// </summary>
    public static async Task<RestoreResult> RestoreAsync(
        ILocalStore store,
        LibraryBackup backup,
        RestoreMode mode,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(backup);

        var data = Consistent(backup);
        if (backup.OwnerId != ownerId)
        {
            Rekey(data);
        }

        var run = new RestoreRun(store, mode, ownerId, now, ct);
        await run.RestoreAsync(data.Books);
        var items = await run.RestoreAsync(data.Items);
        await run.RestoreAsync(data.Sessions);
        await run.RestoreAsync(data.Notes);
        await run.RestoreAsync(data.Bookcases);
        await run.RestoreAsync(data.Shelves);
        await run.RestoreAsync(data.Placements);

        return new RestoreResult(items.Added, items.Updated, items.Kept, items.Removed);
    }

    /// <summary>The backup's records, without any that point at a record the file doesn't have.</summary>
    private sealed record BackupData(
        List<Book> Books,
        List<LibraryItem> Items,
        List<ReadingSession> Sessions,
        List<Note> Notes,
        List<Bookcase> Bookcases,
        List<Shelf> Shelves,
        List<ShelfPlacement> Placements);

    private static BackupData Consistent(LibraryBackup backup)
    {
        var books = (backup.Books ?? []).DistinctBy(b => b.Id).ToList();
        var bookIds = books.Select(b => b.Id).ToHashSet();
        var items = (backup.LibraryItems ?? []).Where(i => bookIds.Contains(i.BookId)).DistinctBy(i => i.Id).ToList();
        var itemIds = items.Select(i => i.Id).ToHashSet();
        var bookcases = (backup.Bookcases ?? []).DistinctBy(b => b.Id).ToList();
        var bookcaseIds = bookcases.Select(b => b.Id).ToHashSet();
        var shelves = (backup.Shelves ?? []).Where(s => bookcaseIds.Contains(s.BookcaseId)).DistinctBy(s => s.Id).ToList();
        var shelfIds = shelves.Select(s => s.Id).ToHashSet();

        return new BackupData(
            books,
            items,
            (backup.ReadingSessions ?? []).Where(s => itemIds.Contains(s.LibraryItemId)).DistinctBy(s => s.Id).ToList(),
            (backup.Notes ?? []).Where(n => itemIds.Contains(n.LibraryItemId)).DistinctBy(n => n.Id).ToList(),
            bookcases,
            shelves,
            (backup.Placements ?? [])
                .Where(p => shelfIds.Contains(p.ShelfId) && itemIds.Contains(p.LibraryItemId))
                .DistinctBy(p => p.Id)
                .ToList());
    }

    /// <summary>New ids for every record, with references rewritten to match.</summary>
    private static void Rekey(BackupData data)
    {
        var map = new Dictionary<Guid, Guid>();
        Guid Map(Guid id) => map.TryGetValue(id, out var mapped) ? mapped : id;

        foreach (var entity in data.Books.Cast<SyncEntity>()
            .Concat(data.Items).Concat(data.Sessions).Concat(data.Notes)
            .Concat(data.Bookcases).Concat(data.Shelves).Concat(data.Placements))
        {
            map[entity.Id] = Guid.CreateVersion7();
            entity.Id = map[entity.Id];
        }

        foreach (var item in data.Items)
        {
            item.BookId = Map(item.BookId);
            item.TagIds = []; // tags aren't backed up
            item.ShelfId = null; // legacy (Phase 2–6a), never read
            item.ShelfRank = null;
        }

        foreach (var session in data.Sessions)
        {
            session.LibraryItemId = Map(session.LibraryItemId);
        }

        foreach (var note in data.Notes)
        {
            note.LibraryItemId = Map(note.LibraryItemId);
        }

        foreach (var shelf in data.Shelves)
        {
            shelf.BookcaseId = Map(shelf.BookcaseId);
        }

        foreach (var placement in data.Placements)
        {
            placement.ShelfId = Map(placement.ShelfId);
            placement.LibraryItemId = Map(placement.LibraryItemId);
        }
    }

    private sealed record TableCounts(int Added, int Updated, int Kept, int Removed);

    private sealed class RestoreRun(ILocalStore store, RestoreMode mode, Guid ownerId, DateTimeOffset now, CancellationToken ct)
    {
        public async Task<TableCounts> RestoreAsync<T>(List<T> records)
            where T : SyncEntity
        {
            var local = (await store.GetAllAsync<T>(includeDeleted: true, ct)).ToDictionary(e => e.Id);
            var write = new List<T>();
            int added = 0, updated = 0, kept = 0, removed = 0;

            foreach (var record in records)
            {
                record.UserId = ownerId;
                record.IsDeleted = false;
                if (record.UpdatedAt > now)
                {
                    record.UpdatedAt = now;
                }

                local.TryGetValue(record.Id, out var existing);
                if (mode == RestoreMode.Merge && existing is not null && existing.UpdatedAt >= record.UpdatedAt)
                {
                    kept++;
                    continue;
                }

                // Based on the version this device has seen, so the push isn't taken for a conflict.
                record.Version = existing?.Version ?? 0;
                if (mode == RestoreMode.Replace)
                {
                    record.UpdatedAt = now; // the restore is the latest word, on every device
                }

                if (existing is null || existing.IsDeleted)
                {
                    added++;
                }
                else
                {
                    updated++;
                }

                write.Add(record);
            }

            if (mode == RestoreMode.Replace)
            {
                var inBackup = records.Select(r => r.Id).ToHashSet();
                foreach (var existing in local.Values.Where(e => !e.IsDeleted && !inBackup.Contains(e.Id)))
                {
                    existing.IsDeleted = true;
                    existing.UpdatedAt = now;
                    write.Add(existing);
                    removed++;
                }
            }

            if (write.Count > 0)
            {
                await store.UpsertManyAsync(write, ct);
            }

            return new TableCounts(added, updated, kept, removed);
        }
    }
}
