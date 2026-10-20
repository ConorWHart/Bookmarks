using Bookmarks.Core.Lookup;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Core.Transfer;

/// <summary>What a Goodreads import will do (shown before importing) and the records it writes.</summary>
/// <param name="NewBooks">Books not in the library yet.</param>
/// <param name="ExistingUpdated">Books already in the library (same ISBN, or same title and author) that get a rating,
/// reads or notes they were missing.</param>
/// <param name="ExistingUnchanged">Books already in the library with nothing to add.</param>
/// <param name="Wishlist">New books that go on the wishlist.</param>
public sealed record GoodreadsImportPlan(
    int Rows,
    int NewBooks,
    int ExistingUpdated,
    int ExistingUnchanged,
    int Read,
    int CurrentlyReading,
    int ToRead,
    int Wishlist)
{
    internal List<Book> Books { get; } = [];

    internal List<LibraryItem> Items { get; } = [];

    internal List<ReadingSession> Sessions { get; } = [];

    internal List<Note> Notes { get; } = [];

    /// <summary>Ids of the new library items, for "Find covers" afterwards.</summary>
    public IReadOnlyList<Guid> NewItemIds { get; internal set; } = [];
}

/// <summary>Goodreads export → library (Phase 7b). Planning reads the library; nothing is written until <see cref="ApplyAsync"/>.</summary>
/// <remarks>
/// <list type="bullet">
/// <item>Shelf "read" → finished reads (Read Count of them, at least one; the latest dated Date Read). "currently-reading"
/// → a read in progress. "to-read" → <c>toReadStatus</c> (Wishlist or Owned), but Owned whenever Owned Copies &gt; 0.
/// A "did-not-finish"-style shelf → a stopped read. Everything else is Owned.</item>
/// <item>My Rating → the copy's rating; Private Notes and My Review → notes; Date Added → added date.</item>
/// <item>A book already in the library (ISBN, else title + first author; also a repeat within the file) isn't added again:
/// it only gets what it lacks (rating, reads when it has none, notes it doesn't have yet), so importing twice is harmless.</item>
/// </list>
/// </remarks>
public static class GoodreadsImport
{
    public static async Task<GoodreadsImportPlan> PlanAsync(
        ILocalStore store,
        IReadOnlyList<GoodreadsBook> rows,
        OwnershipStatus toReadStatus,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(rows);

        // The library as it stands, keyed for matching.
        var books = (await store.GetBooksAsync(ct)).Where(b => !b.IsDeleted).ToDictionary(b => b.Id);
        var sessions = (await store.GetAllAsync<ReadingSession>(ct: ct)).ToLookup(s => s.LibraryItemId);
        var notes = (await store.GetAllAsync<Note>(ct: ct)).ToLookup(n => n.LibraryItemId);
        var targets = new Dictionary<string, Target>(StringComparer.Ordinal);
        foreach (var item in (await store.GetAllAsync<LibraryItem>(ct: ct)).Where(i => books.ContainsKey(i.BookId)).OrderBy(i => i.AddedAt))
        {
            // A copy: planning must not change the library (the in-memory store hands out its own instances).
            var copy = (LibraryItem)SyncEntityTypes.LibraryItem.FromPayload(SyncEntityTypes.LibraryItem.ToPayload(item));
            var target = new Target(books[item.BookId], copy, isNew: false)
            {
                Sessions = sessions[item.Id].ToList(),
                Notes = notes[item.Id].ToList(),
            };
            foreach (var key in Keys(target.Book))
            {
                targets.TryAdd(key, target);
            }
        }

        var added = new List<Target>();
        var touched = new HashSet<Target>();
        var matched = new HashSet<Target>();
        int read = 0, reading = 0, toRead = 0, wishlist = 0;

        foreach (var row in rows)
        {
            switch (row.ExclusiveShelf)
            {
                case GoodreadsBook.Read: read++; break;
                case GoodreadsBook.CurrentlyReading: reading++; break;
                case GoodreadsBook.ToRead: toRead++; break;
            }

            var rowBook = NewBook(row, ownerId);
            var existing = Keys(rowBook).Select(k => targets.GetValueOrDefault(k)).FirstOrDefault(t => t is not null);
            if (existing is not null)
            {
                if (!existing.IsNew)
                {
                    matched.Add(existing);
                }

                if (FillIn(existing, row, ownerId, now))
                {
                    touched.Add(existing);
                }

                continue;
            }

            var status = row.ExclusiveShelf == GoodreadsBook.ToRead && row.OwnedCopies == 0 ? toReadStatus : OwnershipStatus.Owned;
            if (status == OwnershipStatus.Wishlist)
            {
                wishlist++;
            }

            var newItem = new LibraryItem
            {
                UserId = ownerId,
                BookId = rowBook.Id,
                Status = status,
                Format = row.Format ?? BookFormat.Paperback,
                Rating = row.Rating,
                AddedAt = row.DateAdded is { } date ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : now,
                WantToReread = row.ExclusiveShelf == GoodreadsBook.ToRead && row.ReadCount > 0,
                UpdatedAt = now,
            };
            var created = new Target(rowBook, newItem, isNew: true);
            FillIn(created, row, ownerId, now);
            added.Add(created);
            foreach (var key in Keys(rowBook))
            {
                targets.TryAdd(key, created);
            }
        }

        var updatedExisting = touched.Count(t => !t.IsNew);
        var plan = new GoodreadsImportPlan(
            rows.Count,
            added.Count,
            updatedExisting,
            matched.Count - updatedExisting,
            read,
            reading,
            toRead,
            wishlist);

        foreach (var target in added)
        {
            plan.Books.Add(target.Book);
            plan.Items.Add(target.Item);
        }

        foreach (var target in touched.Where(t => !t.IsNew))
        {
            target.Item.UpdatedAt = now;
            plan.Items.Add(target.Item);
        }

        foreach (var target in added.Concat(touched.Where(t => !t.IsNew)))
        {
            plan.Sessions.AddRange(target.NewSessions);
            plan.Notes.AddRange(target.NewNotes);
        }

        plan.NewItemIds = added.Select(t => t.Item.Id).ToList();
        return plan;
    }

    /// <summary>Writes the plan's records as local edits (they sync like any other change).</summary>
    public static async Task ApplyAsync(ILocalStore store, GoodreadsImportPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(plan);

        await WriteAsync(store, plan.Books, ct);
        await WriteAsync(store, plan.Items, ct);
        await WriteAsync(store, plan.Sessions, ct);
        await WriteAsync(store, plan.Notes, ct);
    }

    private static async Task WriteAsync<T>(ILocalStore store, List<T> records, CancellationToken ct)
        where T : SyncEntity
    {
        if (records.Count > 0)
        {
            await store.UpsertManyAsync(records, ct);
        }
    }

    private static Book NewBook(GoodreadsBook row, Guid ownerId) => new()
    {
        UserId = ownerId,
        Title = row.Title,
        Authors = row.Authors.ToList(),
        Isbn13 = row.Isbn13,
        Publisher = row.Publisher,
        PublishedYear = row.PublishedYear,
        PageCount = row.PageCount,
        Series = row.Series,
        SeriesNumber = row.SeriesNumber,
        Source = BookSource.Manual,
    };

    /// <summary>Match keys: the ISBN, and title + first author (for books without one, or a different edition's ISBN).</summary>
    private static IEnumerable<string> Keys(Book book)
    {
        if (Isbn.TryNormalize(book.Isbn13, out var isbn))
        {
            yield return "isbn:" + isbn;
        }

        var title = CatalogText.NormalizeTitle(book.Title);
        if (title.Length > 0)
        {
            yield return "title:" + title + "|" + CatalogText.NormalizeTitle(book.PrimaryAuthor);
        }
    }

    /// <summary>Adds what the target lacks from the row. Returns true when anything was added.</summary>
    private static bool FillIn(Target target, GoodreadsBook row, Guid ownerId, DateTimeOffset now)
    {
        var changed = false;

        if (target.Item.Rating is null && row.Rating is { } rating)
        {
            target.Item.Rating = rating;
            changed = true;
        }

        var live = target.Sessions.Where(s => !s.IsDeleted).ToList();
        if (live.Count == 0)
        {
            foreach (var session in SessionsFor(row, target.Item.Id, ownerId, now))
            {
                target.Add(session);
                changed = true;
            }
        }

        foreach (var body in NotesFor(row))
        {
            if (!target.Notes.Any(n => !n.IsDeleted && string.Equals(n.Body.Trim(), body, StringComparison.Ordinal)))
            {
                target.Add(new Note { UserId = ownerId, LibraryItemId = target.Item.Id, Body = body, CreatedAt = now, UpdatedAt = now });
                changed = true;
            }
        }

        return changed;
    }

    private static IEnumerable<ReadingSession> SessionsFor(GoodreadsBook row, Guid itemId, Guid ownerId, DateTimeOffset now)
    {
        ReadingSession Session(ReadingOutcome outcome, DateOnly? finishedOn)
            => new() { UserId = ownerId, LibraryItemId = itemId, Outcome = outcome, FinishedOn = finishedOn, UpdatedAt = now };

        if (row.ExclusiveShelf == GoodreadsBook.CurrentlyReading)
        {
            yield return Session(ReadingOutcome.InProgress, null);
            if (row.DateRead is { } earlier)
            {
                yield return Session(ReadingOutcome.Finished, earlier); // a re-read: the earlier read is done
            }

            yield break;
        }

        if (row.IsDidNotFinish)
        {
            yield return Session(ReadingOutcome.Abandoned, row.DateRead);
            yield break;
        }

        // "read" counts at least once; any other shelf (to-read with a Read Count is a planned re-read) counts what it says.
        var finished = row.ExclusiveShelf == GoodreadsBook.Read ? Math.Max(1, row.ReadCount) : row.ReadCount;
        for (var i = 0; i < finished; i++)
        {
            // Goodreads only keeps the latest date; earlier reads are undated.
            yield return Session(ReadingOutcome.Finished, i == 0 ? row.DateRead : null);
        }
    }

    private static IEnumerable<string> NotesFor(GoodreadsBook row)
    {
        if (row.PrivateNotes is { Length: > 0 } notes)
        {
            yield return notes.Trim();
        }

        if (row.Review is { Length: > 0 } review)
        {
            yield return "My Goodreads review:\n\n" + review.Trim();
        }
    }

    /// <summary>A copy in the library, or one this import adds, with the records it gains.</summary>
    private sealed class Target(Book book, LibraryItem item, bool isNew)
    {
        public Book Book { get; } = book;

        public LibraryItem Item { get; } = item;

        public bool IsNew { get; } = isNew;

        public List<ReadingSession> Sessions { get; init; } = [];

        public List<Note> Notes { get; init; } = [];

        public List<ReadingSession> NewSessions { get; } = [];

        public List<Note> NewNotes { get; } = [];

        public void Add(ReadingSession session)
        {
            Sessions.Add(session);
            NewSessions.Add(session);
        }

        public void Add(Note note)
        {
            Notes.Add(note);
            NewNotes.Add(note);
        }
    }
}
