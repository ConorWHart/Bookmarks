using Bookmarks.Core.Library;
using Bookmarks.Core.Ranking;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.UI.Services;

/// <summary>
/// Loads the local store into an in-memory list of <see cref="LibraryEntry"/> that the library views
/// query, and is the single place the UI writes through. Every write updates the store, then reloads
/// and raises <see cref="Changed"/> (later also after each sync pull).
/// </summary>
public sealed class LibraryService(ILocalStore store, IAccountService account)
{
    /// <summary>
    /// Owner of records created from now on: the signed-in user, or <see cref="Guid.Empty"/> in guest mode.
    /// Each account has its own local database, so a library never mixes two users' records.
    /// </summary>
    public Guid CurrentUserId => account.Current.UserId;

    private IReadOnlyList<LibraryEntry> _entries = [];
    private IReadOnlyList<Bookcase> _bookcases = [];
    private IReadOnlyList<Shelf> _shelves = [];
    private ILookup<Guid, ReadingSession> _sessions = Array.Empty<ReadingSession>().ToLookup(s => s.LibraryItemId);
    private ILookup<Guid, Note> _notes = Array.Empty<Note>().ToLookup(n => n.LibraryItemId);
    private bool _loaded;

    public event Action? Changed;

    public IReadOnlyList<LibraryEntry> Entries => _entries;

    /// <summary>Ordered by rank.</summary>
    public IReadOnlyList<Bookcase> Bookcases => _bookcases;

    /// <summary>Ordered by rank.</summary>
    public IReadOnlyList<Shelf> ShelvesIn(Guid bookcaseId) => _shelves.Where(s => s.BookcaseId == bookcaseId).ToList();

    /// <summary>True when there are no books and no bookcases (first run).</summary>
    public bool IsEmpty => _entries.Count == 0 && _bookcases.Count == 0;

    public LibraryEntry? Find(Guid itemId) => _entries.FirstOrDefault(e => e.Id == itemId);

    /// <summary>Newest first (by finish date, then start date).</summary>
    public IReadOnlyList<ReadingSession> SessionsFor(Guid itemId)
        => _sessions[itemId]
            .OrderByDescending(s => s.FinishedOn ?? s.StartedOn ?? DateOnly.MaxValue)
            .ThenByDescending(s => s.Id)
            .ToList();

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Note> NotesFor(Guid itemId)
        => _notes[itemId].OrderByDescending(n => n.CreatedAt).ToList();

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return;
        }

        await ReloadAsync(ct);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var books = (await store.GetBooksAsync(ct)).ToDictionary(b => b.Id);
        var items = await store.GetAllAsync<LibraryItem>(ct: ct);
        var sessions = await store.GetAllAsync<ReadingSession>(ct: ct);
        _sessions = sessions.ToLookup(s => s.LibraryItemId);
        _notes = (await store.GetAllAsync<Note>(ct: ct)).ToLookup(n => n.LibraryItemId);

        _entries = items
            .Where(i => books.ContainsKey(i.BookId))
            .Select(i => LibraryEntry.Create(i, books[i.BookId], _sessions[i.Id]))
            .ToList();

        _bookcases = (await store.GetAllAsync<Bookcase>(ct: ct))
            .OrderBy(b => b.SortRank, StringComparer.Ordinal)
            .ToList();
        _shelves = (await store.GetAllAsync<Shelf>(ct: ct))
            .OrderBy(s => s.SortRank, StringComparer.Ordinal)
            .ToList();

        _loaded = true;
        Changed?.Invoke();
    }

    /// <summary>Opt-in sample library (one bookcase, 20 books). Only runs on an empty library.</summary>
    public async Task LoadDemoLibraryAsync(CancellationToken ct = default)
    {
        await ReloadAsync(ct);
        if (!IsEmpty)
        {
            return;
        }

        await SampleData.SeedAsync(store, CurrentUserId, ct);
        await ReloadAsync(ct);
    }

    // ---------- Books ----------

    /// <summary>Adds a manually entered book at the end of its shelf. Returns the new item's id.</summary>
    public async Task<Guid> AddBookAsync(BookEditModel model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var book = new Book { Source = BookSource.Manual, CreatedByUserId = CurrentUserId };
        var item = new LibraryItem { UserId = CurrentUserId, BookId = book.Id };
        model.ApplyTo(book, item);
        item.ShelfRank = NextRankOn(item.ShelfId);

        await store.UpsertBookAsync(book, ct);
        await store.UpsertAsync(item, ct);

        var today = Today;
        if (model.ReadState == InitialReadState.Reading)
        {
            await store.UpsertAsync(NewSession(item.Id, ReadingOutcome.InProgress, startedOn: today), ct);
        }
        else if (model.ReadState == InitialReadState.Read)
        {
            // Dates unknown; the user can fill them in from the reading log.
            await store.UpsertAsync(NewSession(item.Id, ReadingOutcome.Finished), ct);
        }

        await ReloadAsync(ct);
        return item.Id;
    }

    /// <summary>Saves edits to an existing book. Moving to another shelf places it at the end of that shelf.</summary>
    public async Task UpdateBookAsync(Guid itemId, BookEditModel model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Re-read so the edit applies to the stored version, not a possibly stale cached one.
        var item = await store.GetAsync<LibraryItem>(itemId, ct) ?? throw new InvalidOperationException("Book not found.");
        var book = (await store.GetBooksAsync(ct)).FirstOrDefault(b => b.Id == item.BookId)
            ?? throw new InvalidOperationException("Catalog record not found.");

        var previousShelf = item.ShelfId;
        model.ApplyTo(book, item);
        if (item.ShelfId != previousShelf)
        {
            item.ShelfRank = NextRankOn(item.ShelfId);
        }

        // TODO(Phase 5): catalog books (Open Library / Google Books) are shared; edits to them should
        // become per-copy overrides instead of changing the shared record.
        await store.UpsertBookAsync(book, ct);
        item.Touch();
        await store.UpsertAsync(item, ct);
        await ReloadAsync(ct);
    }

    /// <summary>
    /// Soft delete (tombstone, so it syncs). Sessions and notes are left as they are: they're hidden
    /// with the book, and <see cref="RestoreBookAsync"/> brings everything back.
    /// </summary>
    public Task DeleteBookAsync(Guid itemId, CancellationToken ct = default)
        => UpdateAsync<LibraryItem>(itemId, i => i.IsDeleted = true, ct);

    public Task RestoreBookAsync(Guid itemId, CancellationToken ct = default)
        => UpdateAsync<LibraryItem>(itemId, i => i.IsDeleted = false, ct);

    /// <summary>0.5–5, or null to clear.</summary>
    public Task SetRatingAsync(Guid itemId, decimal? rating, CancellationToken ct = default)
        => UpdateAsync<LibraryItem>(itemId, i => i.Rating = rating, ct);

    public Task SetWantToRereadAsync(Guid itemId, bool value, CancellationToken ct = default)
        => UpdateAsync<LibraryItem>(itemId, i => i.WantToReread = value, ct);

    // ---------- Reading sessions ----------

    public async Task StartReadingAsync(Guid itemId, CancellationToken ct = default)
    {
        await store.UpsertAsync(NewSession(itemId, ReadingOutcome.InProgress, startedOn: Today), ct);
        await ReloadAsync(ct);
    }

    /// <summary>Finishes the most recent in-progress session (or logs a finished read if there is none).</summary>
    public async Task FinishReadingAsync(Guid itemId, ReadingOutcome outcome = ReadingOutcome.Finished, CancellationToken ct = default)
    {
        var session = _sessions[itemId]
            .Where(s => s.Outcome == ReadingOutcome.InProgress)
            .OrderByDescending(s => s.StartedOn ?? DateOnly.MinValue)
            .FirstOrDefault()
            ?? NewSession(itemId, ReadingOutcome.InProgress);

        session.Outcome = outcome;
        session.FinishedOn = Today;
        session.Touch();
        await store.UpsertAsync(session, ct);
        await ReloadAsync(ct);
    }

    /// <summary>Adds or updates a session (from the reading-session dialog).</summary>
    public async Task SaveSessionAsync(ReadingSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.UserId = CurrentUserId;
        session.Touch();
        await store.UpsertAsync(session, ct);
        await ReloadAsync(ct);
    }

    public Task DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
        => UpdateAsync<ReadingSession>(sessionId, s => s.IsDeleted = true, ct);

    // ---------- Notes ----------

    public async Task AddNoteAsync(Guid itemId, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        await store.UpsertAsync(new Note { UserId = CurrentUserId, LibraryItemId = itemId, Body = body.Trim() }, ct);
        await ReloadAsync(ct);
    }

    public Task UpdateNoteAsync(Guid noteId, string body, CancellationToken ct = default)
        => UpdateAsync<Note>(noteId, n => n.Body = body.Trim(), ct);

    public Task DeleteNoteAsync(Guid noteId, CancellationToken ct = default)
        => UpdateAsync<Note>(noteId, n => n.IsDeleted = true, ct);

    // ---------- Bookcases & shelves ----------

    public const int DefaultShelfCount = 3;
    public const int DefaultShelfWidthMm = 800;
    public const int MinShelfWidthMm = 100;
    public const int MaxShelfWidthMm = 3000;

    /// <summary>Bookcase finishes, as used by <c>BookcaseFrame</c> (key, label).</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Themes =
    [
        ("oak", "Oak"),
        ("walnut", "Walnut"),
        ("white", "White"),
        ("dark", "Dark"),
    ];

    public bool IsKnownShelf(Guid? shelfId) => shelfId is { } id && _shelves.Any(s => s.Id == id);

    /// <summary>
    /// Books on a shelf in shelf order. Null (or a shelf that no longer exists) = the Unshelved group.
    /// </summary>
    public IReadOnlyList<LibraryEntry> BooksOnShelf(Guid? shelfId)
    {
        var entries = IsKnownShelf(shelfId)
            ? _entries.Where(e => e.Item.ShelfId == shelfId)
            : _entries.Where(e => !IsKnownShelf(e.Item.ShelfId));
        return LibraryQueryEngine.Apply(entries, LibraryFilter.None, new LibrarySort(LibrarySortField.ShelfOrder));
    }

    /// <summary>How many books are on a shelf and how much width (mm) they take up.</summary>
    public (int Count, int UsedMm) ShelfUsage(Guid shelfId)
    {
        var books = BooksOnShelf(shelfId);
        var row = ShelfLayoutEngine.Flow(books, int.MaxValue).FirstOrDefault();
        return (books.Count, row?.UsedWidthMm ?? 0);
    }

    /// <summary>Adds a bookcase at the end with <see cref="DefaultShelfCount"/> shelves. Returns its id.</summary>
    public async Task<Guid> AddBookcaseAsync(CancellationToken ct = default)
    {
        var bookcase = new Bookcase
        {
            UserId = CurrentUserId,
            Name = $"Bookcase {_bookcases.Count + 1}",
            Theme = _bookcases.LastOrDefault()?.Theme ?? "oak",
            SortRank = RankKey.Between(LastRank(_bookcases.Select(b => b.SortRank)), null),
        };
        await store.UpsertAsync(bookcase, ct);

        var ranks = RankKey.Sequence(DefaultShelfCount);
        for (var i = 0; i < ranks.Count; i++)
        {
            await store.UpsertAsync(
                new Shelf { UserId = CurrentUserId, BookcaseId = bookcase.Id, Name = $"Shelf {i + 1}", SortRank = ranks[i], WidthMm = DefaultShelfWidthMm },
                ct);
        }

        await ReloadAsync(ct);
        return bookcase.Id;
    }

    public Task RenameBookcaseAsync(Guid bookcaseId, string? name, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(name)
            ? Task.CompletedTask
            : UpdateAsync<Bookcase>(bookcaseId, b => b.Name = name!.Trim(), ct);

    public Task SetBookcaseThemeAsync(Guid bookcaseId, string theme, CancellationToken ct = default)
        => Themes.Any(t => t.Key == theme)
            ? UpdateAsync<Bookcase>(bookcaseId, b => b.Theme = theme, ct)
            : Task.CompletedTask;

    /// <summary>Moves a bookcase earlier (-1) or later (+1) in the room.</summary>
    public Task MoveBookcaseAsync(Guid bookcaseId, int delta, CancellationToken ct = default)
        => MoveAsync(_bookcases, bookcaseId, delta, b => b.SortRank, (b, key) => b.SortRank = key, ct);

    /// <summary>Soft-deletes a bookcase and its shelves; their books move to Unshelved (keeping their order).</summary>
    public async Task DeleteBookcaseAsync(Guid bookcaseId, CancellationToken ct = default)
    {
        foreach (var shelf in ShelvesIn(bookcaseId))
        {
            await UnshelveBooksAsync(shelf.Id, ct);
            shelf.IsDeleted = true;
            shelf.Touch();
            await store.UpsertAsync(shelf, ct);
        }

        await UpdateAsync<Bookcase>(bookcaseId, b => b.IsDeleted = true, ct);
    }

    /// <summary>Adds a shelf at the bottom of a bookcase, as wide as the one above it.</summary>
    public async Task AddShelfAsync(Guid bookcaseId, CancellationToken ct = default)
    {
        var shelves = ShelvesIn(bookcaseId);
        await store.UpsertAsync(
            new Shelf
            {
                UserId = CurrentUserId,
                BookcaseId = bookcaseId,
                Name = $"Shelf {shelves.Count + 1}",
                SortRank = RankKey.Between(LastRank(shelves.Select(s => s.SortRank)), null),
                WidthMm = shelves.LastOrDefault()?.WidthMm ?? DefaultShelfWidthMm,
            },
            ct);
        await ReloadAsync(ct);
    }

    /// <summary>Blank clears the name.</summary>
    public Task RenameShelfAsync(Guid shelfId, string? name, CancellationToken ct = default)
        => UpdateAsync<Shelf>(shelfId, s => s.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(), ct);

    /// <summary>Null = unknown width (no capacity shown).</summary>
    public Task SetShelfWidthAsync(Guid shelfId, int? widthMm, CancellationToken ct = default)
        => UpdateAsync<Shelf>(shelfId, s => s.WidthMm = widthMm is { } w ? Math.Clamp(w, MinShelfWidthMm, MaxShelfWidthMm) : null, ct);

    /// <summary>Moves a shelf up (-1) or down (+1) within its bookcase.</summary>
    public Task MoveShelfAsync(Guid shelfId, int delta, CancellationToken ct = default)
    {
        var shelf = _shelves.FirstOrDefault(s => s.Id == shelfId);
        return shelf is null
            ? Task.CompletedTask
            : MoveAsync(ShelvesIn(shelf.BookcaseId), shelfId, delta, s => s.SortRank, (s, key) => s.SortRank = key, ct);
    }

    /// <summary>Soft-deletes a shelf; its books move to Unshelved.</summary>
    public async Task DeleteShelfAsync(Guid shelfId, CancellationToken ct = default)
    {
        await UnshelveBooksAsync(shelfId, ct);
        await UpdateAsync<Shelf>(shelfId, s => s.IsDeleted = true, ct);
    }

    /// <summary>Puts a book at the end of another shelf (null = Unshelved).</summary>
    public async Task MoveBookToShelfAsync(Guid itemId, Guid? shelfId, CancellationToken ct = default)
    {
        var target = IsKnownShelf(shelfId) ? shelfId : null;
        var rank = NextRankOn(target);
        await UpdateAsync<LibraryItem>(
            itemId,
            i =>
            {
                i.ShelfId = target;
                i.ShelfRank = rank;
            },
            ct);
    }

    /// <summary>Moves a book left (-1) or right (+1) on its shelf.</summary>
    public Task MoveBookWithinShelfAsync(Guid itemId, int delta, CancellationToken ct = default)
    {
        var entry = Find(itemId);
        return entry is null
            ? Task.CompletedTask
            : MoveAsync(BooksOnShelf(entry.Item.ShelfId).Select(e => e.Item).ToList(), itemId, delta, i => i.ShelfRank, (i, key) => i.ShelfRank = key, ct);
    }

    /// <summary>Moves every book on a shelf to the end of Unshelved, keeping their order. Doesn't reload.</summary>
    private async Task UnshelveBooksAsync(Guid shelfId, CancellationToken ct)
    {
        string? last = null;
        foreach (var entry in BooksOnShelf(shelfId).ToList())
        {
            last = last is null ? NextRankOn(null) : RankKey.Between(last, null);
            entry.Item.ShelfId = null;
            entry.Item.ShelfRank = last;
            entry.Item.Touch();
            await store.UpsertAsync(entry.Item, ct);
        }
    }

    /// <summary>Reorders one entity in a rank-ordered list, writing only the keys that change.</summary>
    private async Task MoveAsync<T>(IReadOnlyList<T> ordered, Guid id, int delta, Func<T, string?> getKey, Action<T, string> setKey, CancellationToken ct)
        where T : SyncEntity
    {
        var from = ordered.ToList().FindIndex(x => x.Id == id);
        if (from < 0)
        {
            return;
        }

        var writes = Reorder.Move(ordered.Select(getKey).ToList(), from, from + delta);
        foreach (var (index, key) in writes)
        {
            var entity = ordered[index];
            setKey(entity, key);
            entity.Touch();
            await store.UpsertAsync(entity, ct);
        }

        if (writes.Count > 0)
        {
            await ReloadAsync(ct);
        }
    }

    private static string? LastRank(IEnumerable<string?> ranks)
        => ranks.Where(r => !string.IsNullOrEmpty(r)).Max(StringComparer.Ordinal);

    // ---------- Helpers ----------

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private ReadingSession NewSession(Guid itemId, ReadingOutcome outcome, DateOnly? startedOn = null)
        => new() { UserId = CurrentUserId, LibraryItemId = itemId, Outcome = outcome, StartedOn = startedOn };

    /// <summary>A rank after every book currently on <paramref name="shelfId"/> (null = Unshelved).</summary>
    private string NextRankOn(Guid? shelfId)
    {
        var last = _entries
            .Where(e => e.Item.ShelfId == shelfId && e.Item.ShelfRank is not null)
            .Select(e => e.Item.ShelfRank)
            .Max(StringComparer.Ordinal);
        return RankKey.Between(last, null);
    }

    /// <summary>Read-modify-write of one record, then <see cref="SyncEntity.Touch"/>, save and reload.</summary>
    private async Task UpdateAsync<T>(Guid id, Action<T> change, CancellationToken ct)
        where T : SyncEntity
    {
        var entity = await store.GetAsync<T>(id, ct);
        if (entity is null)
        {
            return;
        }

        change(entity);
        entity.Touch();
        await store.UpsertAsync(entity, ct);
        await ReloadAsync(ct);
    }
}
