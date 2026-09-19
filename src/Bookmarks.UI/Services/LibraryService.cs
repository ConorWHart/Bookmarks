using Bookmarks.Core.Library;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Domain;

namespace Bookmarks.UI.Services;

/// <summary>
/// Loads the local store into an in-memory list of <see cref="LibraryEntry"/> that the
/// library views query. Reloaded after local edits and (later) after each sync pull.
/// </summary>
public sealed class LibraryService(ILocalStore store)
{
    private IReadOnlyList<LibraryEntry> _entries = [];
    private IReadOnlyList<Bookcase> _bookcases = [];
    private IReadOnlyList<Shelf> _shelves = [];
    private bool _loaded;

    public event Action? Changed;

    public IReadOnlyList<LibraryEntry> Entries => _entries;

    /// <summary>Ordered by rank.</summary>
    public IReadOnlyList<Bookcase> Bookcases => _bookcases;

    /// <summary>Ordered by rank.</summary>
    public IReadOnlyList<Shelf> ShelvesIn(Guid bookcaseId) => _shelves.Where(s => s.BookcaseId == bookcaseId).ToList();

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_loaded)
        {
            return;
        }

        // TODO(Phase 2): remove once add-book flows exist. Seeds demo data into an empty store.
        if ((await store.GetAllAsync<LibraryItem>(ct: ct)).Count == 0)
        {
            await SampleData.SeedAsync(store, ct);
        }

        await ReloadAsync(ct);
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var books = (await store.GetBooksAsync(ct)).ToDictionary(b => b.Id);
        var items = await store.GetAllAsync<LibraryItem>(ct: ct);
        var sessions = await store.GetAllAsync<ReadingSession>(ct: ct);
        var sessionsByItem = sessions.ToLookup(s => s.LibraryItemId);

        _entries = items
            .Where(i => books.ContainsKey(i.BookId))
            .Select(i => LibraryEntry.Create(i, books[i.BookId], sessionsByItem[i.Id]))
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
}
