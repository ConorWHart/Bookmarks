using Bookmarks.Core.Library;
using Bookmarks.Core.Ranking;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.UI.Services;

/// <summary>
/// Loads the local store into an in-memory list of <see cref="LibraryEntry"/> that the library views
/// query, and is the single place the UI writes through. Every write updates the store, then reloads
/// and raises <see cref="Changed"/> (also after each sync pull that changed something).
/// Since Phase 6b a book's places are <see cref="ShelfPlacement"/> rows: any number of bookcases, once each.
/// </summary>
public sealed class LibraryService(ILocalStore store, IAccountService account)
{
    /// <summary>
    /// Owner of records created from now on: the signed-in user, or <see cref="Guid.Empty"/> in guest mode.
    /// Each account has its own local database, so a library never mixes two users' records.
    /// </summary>
    public Guid CurrentUserId => account.Current.UserId;

    private IReadOnlyList<LibraryEntry> _entries = [];
    private Dictionary<Guid, LibraryEntry> _byId = [];
    private IReadOnlyList<Bookcase> _bookcases = [];
    private IReadOnlyList<Shelf> _shelves = [];
    private ILookup<Guid, ReadingSession> _sessions = Array.Empty<ReadingSession>().ToLookup(s => s.LibraryItemId);
    private ILookup<Guid, Note> _notes = Array.Empty<Note>().ToLookup(n => n.LibraryItemId);
    private IReadOnlyList<ShelfPlacement> _placements = [];
    private IReadOnlyList<string> _authors = [];
    private IReadOnlyList<string> _genres = [];
    private IReadOnlyList<string> _seriesNames = [];
    private bool _loaded;

    public event Action? Changed;

    public IReadOnlyList<LibraryEntry> Entries => _entries;

    /// <summary>Ordered by rank.</summary>
    public IReadOnlyList<Bookcase> Bookcases => _bookcases;

    /// <summary>Ordered by rank.</summary>
    public IReadOnlyList<Shelf> ShelvesIn(Guid bookcaseId) => _shelves.Where(s => s.BookcaseId == bookcaseId).ToList();

    /// <summary>True when there are no books and no bookcases (first run).</summary>
    public bool IsEmpty => _entries.Count == 0 && _bookcases.Count == 0;

    public LibraryEntry? Find(Guid itemId) => _byId.GetValueOrDefault(itemId);

    public Bookcase? FindBookcase(Guid bookcaseId) => _bookcases.FirstOrDefault(b => b.Id == bookcaseId);

    public Shelf? FindShelf(Guid shelfId) => _shelves.FirstOrDefault(s => s.Id == shelfId);

    /// <summary>How many books stand in a bookcase (each counted once, on whichever shelf).</summary>
    public int BookCountIn(Guid bookcaseId)
    {
        var shelves = ShelvesIn(bookcaseId).Select(s => s.Id).ToHashSet();
        return _placements.Count(p => shelves.Contains(p.ShelfId) && _byId.ContainsKey(p.LibraryItemId));
    }

    /// <summary>Bookcases not drawn in the Bookcases view.</summary>
    public int HiddenBookcaseCount => _bookcases.Count(b => b.IsHidden);

    /// <summary>Where a book is, ordered like the room: bookcase order, then shelf order.</summary>
    public IReadOnlyList<BookPlace> PlacesOf(Guid itemId)
        => _placements
            .Where(p => p.LibraryItemId == itemId)
            .Select(p => FindShelf(p.ShelfId) is { } shelf && FindBookcase(shelf.BookcaseId) is { } bookcase
                ? new BookPlace(p, bookcase, shelf)
                : null)
            .OfType<BookPlace>()
            .OrderBy(x => x.Bookcase.SortRank, StringComparer.Ordinal)
            .ThenBy(x => x.Shelf.SortRank, StringComparer.Ordinal)
            .ToList();

    /// <summary>Distinct authors, genres and series in the library (for filter pickers), alphabetical.</summary>
    public IReadOnlyList<string> Authors => _authors;

    public IReadOnlyList<string> Genres => _genres;

    public IReadOnlyList<string> SeriesNames => _seriesNames;

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

        // Guests never sync, so the one-time move to placements can run now (signed-in: after a sync pass).
        if (CurrentUserId == Guid.Empty)
        {
            await MigratePlacementsAsync(ct);
        }
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var books = (await store.GetBooksAsync(ct)).ToDictionary(b => b.Id);
        var items = await store.GetAllAsync<LibraryItem>(ct: ct);
        var sessions = await store.GetAllAsync<ReadingSession>(ct: ct);
        _sessions = sessions.ToLookup(s => s.LibraryItemId);
        _notes = (await store.GetAllAsync<Note>(ct: ct)).ToLookup(n => n.LibraryItemId);

        _bookcases = (await store.GetAllAsync<Bookcase>(ct: ct))
            .OrderBy(b => b.SortRank, StringComparer.Ordinal)
            .ToList();
        _shelves = (await store.GetAllAsync<Shelf>(ct: ct))
            .OrderBy(s => s.SortRank, StringComparer.Ordinal)
            .ToList();

        // Only shelves of live bookcases count; a placement on anything else is ignored.
        var liveBookcases = _bookcases.Select(b => b.Id).ToHashSet();
        _shelves = _shelves.Where(s => liveBookcases.Contains(s.BookcaseId)).ToList();
        var bookcaseOfShelf = _shelves.ToDictionary(s => s.Id, s => s.BookcaseId);

        // One placement per (book, bookcase): duplicates made on two offline devices collapse to the lowest id
        // (the same choice on every device); the next placement edit of that book deletes the extras.
        _placements = (await store.GetAllAsync<ShelfPlacement>(ct: ct))
            .Where(p => bookcaseOfShelf.ContainsKey(p.ShelfId))
            .GroupBy(p => (p.LibraryItemId, BookcaseId: bookcaseOfShelf[p.ShelfId]))
            .Select(g => g.OrderBy(p => p.Id).First())
            .ToList();
        var placementsOf = _placements.ToLookup(p => p.LibraryItemId);

        _entries = items
            .Where(i => books.ContainsKey(i.BookId))
            .Select(i =>
            {
                var shelfIds = placementsOf[i.Id].Select(p => p.ShelfId).ToList();
                return LibraryEntry.Create(i, books[i.BookId], _sessions[i.Id]) with
                {
                    ShelfIds = shelfIds,
                    BookcaseIds = shelfIds.Select(id => bookcaseOfShelf[id]).ToList(),
                };
            })
            .ToList();
        _byId = _entries.ToDictionary(e => e.Id);

        _authors = Distinct(_entries.SelectMany(e => e.Book.Authors));
        _genres = Distinct(_entries.SelectMany(e => e.Book.Genres));
        _seriesNames = Distinct(_entries.Select(e => e.Book.Series));

        _loaded = true;
        Changed?.Invoke();
    }

    /// <summary>Opt-in sample library (two bookcases, 20 books). Only runs on an empty library.</summary>
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

    // ---------- Placements ----------

    /// <summary>
    /// Puts books at the end of <paramref name="shelfId"/>, in the given order. A book already in that bookcase
    /// moves to this shelf (a book is in a bookcase at most once); its places in other bookcases stay.
    /// Returns how many books were placed or moved.
    /// </summary>
    public async Task<int> PlaceBooksAsync(IEnumerable<Guid> itemIds, Guid shelfId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (FindShelf(shelfId) is not { } shelf)
        {
            return 0;
        }

        var all = await store.GetAllAsync<ShelfPlacement>(ct: ct);
        var shelvesOfBookcase = ShelvesIn(shelf.BookcaseId).Select(s => s.Id).ToHashSet();
        var last = LastRank(_placements.Where(p => p.ShelfId == shelfId).Select(p => p.Rank));
        var placed = 0;

        foreach (var itemId in itemIds.Distinct())
        {
            var inBookcase = all.Where(p => p.LibraryItemId == itemId && shelvesOfBookcase.Contains(p.ShelfId)).ToList();
            if (inBookcase.Count == 1 && inBookcase[0].ShelfId == shelfId)
            {
                continue; // already there
            }

            // Moving within the bookcase keeps how it stands (spine or cover).
            var faceOut = inBookcase.Select(p => p.FaceOut).FirstOrDefault(f => f is not null);
            foreach (var old in inBookcase)
            {
                old.IsDeleted = true;
                old.Touch();
                await store.UpsertAsync(old, ct);
            }

            last = RankKey.Between(last, null);
            await store.UpsertAsync(
                new ShelfPlacement { UserId = CurrentUserId, ShelfId = shelfId, LibraryItemId = itemId, Rank = last, FaceOut = faceOut },
                ct);
            placed++;
        }

        if (placed > 0)
        {
            await ReloadAsync(ct);
        }

        return placed;
    }

    /// <summary>Takes a book off a shelf (that bookcase only; it stays in the library and other bookcases).</summary>
    public async Task RemovePlacementAsync(Guid placementId, CancellationToken ct = default)
    {
        var placement = _placements.FirstOrDefault(p => p.Id == placementId);
        if (placement is null || FindShelf(placement.ShelfId) is not { } shelf)
        {
            return;
        }

        // Offline duplicates of the same book in the same bookcase go too.
        var shelvesOfBookcase = ShelvesIn(shelf.BookcaseId).Select(s => s.Id).ToHashSet();
        foreach (var row in (await store.GetAllAsync<ShelfPlacement>(ct: ct))
                     .Where(p => p.LibraryItemId == placement.LibraryItemId && shelvesOfBookcase.Contains(p.ShelfId)))
        {
            row.IsDeleted = true;
            row.Touch();
            await store.UpsertAsync(row, ct);
        }

        await ReloadAsync(ct);
    }

    /// <summary>Moves a placed book left (-1) or right (+1) on its shelf.</summary>
    public Task MovePlacementAsync(Guid placementId, int delta, CancellationToken ct = default)
    {
        var placement = _placements.FirstOrDefault(p => p.Id == placementId);
        return placement is null
            ? Task.CompletedTask
            : MoveAsync(PlacementsOn(placement.ShelfId), placementId, delta, p => p.Rank, (p, key) => p.Rank = key, ct);
    }

    /// <summary>
    /// Runs the one-time move from the legacy <see cref="LibraryItem.ShelfId"/> to placements on this database
    /// (see <see cref="PlacementMigration"/>). Guests run it when the library loads; signed-in devices after a
    /// completed sync pass, so they've pulled what other devices already did. Returns true if it added placements.
    /// </summary>
    public async Task<bool> MigratePlacementsAsync(CancellationToken ct = default)
    {
        if (await store.GetMetaAsync(PlacementMigration.DoneKey, ct) is not null)
        {
            return false;
        }

        var items = await store.GetAllAsync<LibraryItem>(includeDeleted: true, ct: ct);
        var bookcases = (await store.GetAllAsync<Bookcase>(ct: ct)).Select(b => b.Id).ToHashSet();
        var shelves = (await store.GetAllAsync<Shelf>(ct: ct)).Where(s => bookcases.Contains(s.BookcaseId));
        var existing = await store.GetAllAsync<ShelfPlacement>(includeDeleted: true, ct: ct);

        var plan = PlacementMigration.Plan(items, shelves, existing);
        foreach (var placement in plan)
        {
            await store.UpsertAsync(placement, ct);
        }

        await store.SetMetaAsync(PlacementMigration.DoneKey, DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture), ct);
        if (plan.Count > 0)
        {
            await ReloadAsync(ct);
        }

        return plan.Count > 0;
    }

    /// <summary>Placements on a shelf, in shelf order.</summary>
    private List<ShelfPlacement> PlacementsOn(Guid shelfId)
        => _placements
            .Where(p => p.ShelfId == shelfId)
            .OrderBy(p => p.Rank ?? "\uffff", StringComparer.Ordinal)
            .ThenBy(p => p.Id)
            .ToList();

    /// <summary>Soft-deletes every placement on a shelf (its books stay in the library). Doesn't reload.</summary>
    private async Task RemovePlacementsOnShelfAsync(Guid shelfId, CancellationToken ct)
    {
        foreach (var row in (await store.GetAllAsync<ShelfPlacement>(ct: ct)).Where(p => p.ShelfId == shelfId))
        {
            row.IsDeleted = true;
            row.Touch();
            await store.UpsertAsync(row, ct);
        }
    }

    // ---------- Helpers for filter pickers ----------

    private static IReadOnlyList<string> Distinct(IEnumerable<string?> values)
        => values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    // ---------- Books ----------

    /// <summary>Adds a book to the library (not to a shelf: see <see cref="PlaceBooksAsync"/>). Returns the new item's id.</summary>
    public async Task<Guid> AddBookAsync(BookEditModel model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var book = new Book { UserId = CurrentUserId, Source = BookSource.Manual };
        var item = new LibraryItem { UserId = CurrentUserId, BookId = book.Id };
        model.ApplyTo(book, item);

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

    /// <summary>Saves edits to an existing book. Its places on shelves aren't touched.</summary>
    public async Task UpdateBookAsync(Guid itemId, BookEditModel model, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Re-read so the edit applies to the stored version, not a possibly stale cached one.
        var item = await store.GetAsync<LibraryItem>(itemId, ct) ?? throw new InvalidOperationException("Book not found.");
        var book = await store.GetAsync<Book>(item.BookId, ct)
            ?? throw new InvalidOperationException("Book record not found.");

        model.ApplyTo(book, item);

        // Books are per user (Phase 4), so editing one never affects anyone else's copy.
        book.Touch();
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

    /// <summary>Shows the book on the shelf by its cover (face out) or its spine.</summary>
    public Task SetFaceOutAsync(Guid itemId, bool faceOut, CancellationToken ct = default)
        => UpdateAsync<LibraryItem>(itemId, i => i.Appearance.FaceOut = faceOut, ct);

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

    /// <summary>
    /// "Shelf 2": shelves are always numbered by position in their bookcase, top to bottom (stored names aren't
    /// shown). Moving a shelf moves its books; the numbers stay in order.
    /// </summary>
    public string ShelfLabel(Shelf shelf)
    {
        ArgumentNullException.ThrowIfNull(shelf);
        var index = ShelvesIn(shelf.BookcaseId).ToList().FindIndex(s => s.Id == shelf.Id);
        return $"Shelf {index + 1}";
    }

    public bool IsKnownShelf(Guid? shelfId) => shelfId is { } id && _shelves.Any(s => s.Id == id);

    /// <summary>Books on a shelf, in shelf order.</summary>
    public IReadOnlyList<LibraryEntry> BooksOnShelf(Guid shelfId)
    {
        return PlacementsOn(shelfId)
            .Select(p => _byId.GetValueOrDefault(p.LibraryItemId))
            .OfType<LibraryEntry>()
            .ToList();
    }

    /// <summary>The placement of a book on a shelf, if it's there.</summary>
    public ShelfPlacement? PlacementOf(Guid itemId, Guid shelfId)
        => _placements.FirstOrDefault(p => p.LibraryItemId == itemId && p.ShelfId == shelfId);

    /// <summary>How many books are on a shelf and how much width (mm) they take up.</summary>
    public (int Count, int UsedMm) ShelfUsage(Guid shelfId)
    {
        var books = BooksOnShelf(shelfId);
        var row = LayOutShelf(shelfId, books);
        return (books.Count, row?.UsedWidthMm ?? 0);
    }

    /// <summary>Books on a shelf in order, sized for drawing, each spine or cover as set for this shelf.</summary>
    public IReadOnlyList<PlacedBook> PlacedOnShelf(Guid shelfId)
        => LayOutShelf(shelfId, BooksOnShelf(shelfId))?.Books ?? [];

    /// <summary>Whether a book shows its cover on this shelf (its placement's choice, else the book's default).</summary>
    public bool IsFaceOut(ShelfPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return placement.FaceOut ?? Find(placement.LibraryItemId)?.Item.Appearance.FaceOut ?? false;
    }

    /// <summary>Spine or cover for one book on one shelf only.</summary>
    public Task SetPlacementFaceOutAsync(Guid placementId, bool faceOut, CancellationToken ct = default)
        => UpdateAsync<ShelfPlacement>(placementId, p => p.FaceOut = faceOut, ct);

    private ShelfRow? LayOutShelf(Guid shelfId, IReadOnlyList<LibraryEntry> books)
    {
        var faceOut = _placements
            .Where(p => p.ShelfId == shelfId && p.FaceOut is not null)
            .ToDictionary(p => p.LibraryItemId, p => p.FaceOut!.Value);
        return ShelfLayoutEngine.Flow(
                books,
                int.MaxValue,
                isFaceOut: e => faceOut.TryGetValue(e.Id, out var value) ? value : e.Item.Appearance.FaceOut)
            .FirstOrDefault();
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

    /// <summary>Hidden bookcases aren't drawn in the Bookcases view. Synced.</summary>
    public Task SetBookcaseHiddenAsync(Guid bookcaseId, bool hidden, CancellationToken ct = default)
        => UpdateAsync<Bookcase>(bookcaseId, b => b.IsHidden = hidden, ct);

    public Task SetBookcaseThemeAsync(Guid bookcaseId, string theme, CancellationToken ct = default)
        => Themes.Any(t => t.Key == theme)
            ? UpdateAsync<Bookcase>(bookcaseId, b => b.Theme = theme, ct)
            : Task.CompletedTask;

    /// <summary>Moves a bookcase earlier (-1) or later (+1) in the room.</summary>
    public Task MoveBookcaseAsync(Guid bookcaseId, int delta, CancellationToken ct = default)
        => MoveAsync(_bookcases, bookcaseId, delta, b => b.SortRank, (b, key) => b.SortRank = key, ct);

    /// <summary>Soft-deletes a bookcase, its shelves and its placements; the books stay in the library.</summary>
    public async Task DeleteBookcaseAsync(Guid bookcaseId, CancellationToken ct = default)
    {
        foreach (var shelf in ShelvesIn(bookcaseId))
        {
            await RemovePlacementsOnShelfAsync(shelf.Id, ct);
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

    /// <summary>Soft-deletes a shelf and its placements; the books stay in the library (and other bookcases).</summary>
    public async Task DeleteShelfAsync(Guid shelfId, CancellationToken ct = default)
    {
        await RemovePlacementsOnShelfAsync(shelfId, ct);
        await UpdateAsync<Shelf>(shelfId, s => s.IsDeleted = true, ct);
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

/// <summary>One place a book is in: the placement with its bookcase and shelf.</summary>
public sealed record BookPlace(ShelfPlacement Placement, Bookcase Bookcase, Shelf Shelf);
