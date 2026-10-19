namespace Bookmarks.Core.Lookup;

public enum StackItemState
{
    /// <summary>Waiting for its lookup (also while offline).</summary>
    Pending,

    LookingUp,

    Found,

    /// <summary>Open Library has no edition with this ISBN.</summary>
    NotFound,

    /// <summary>The lookup failed (network, server); can be retried.</summary>
    Failed,
}

/// <summary>One scanned ISBN in a stack.</summary>
public sealed class StackItem
{
    internal StackItem(string isbn13, bool alreadyInLibrary)
    {
        Isbn13 = isbn13;
        AlreadyInLibrary = alreadyInLibrary;
        Include = !alreadyInLibrary;
    }

    public string Isbn13 { get; }

    public StackItemState State { get; internal set; } = StackItemState.Pending;

    public BookLookupResult? Result { get; internal set; }

    /// <summary>The library already has a book with this ISBN: left out unless the user ticks it (a second copy).</summary>
    public bool AlreadyInLibrary { get; }

    /// <summary>Add this one (found books only). Defaults to on, except for books already in the library.</summary>
    public bool Include { get; set; }
}

public enum StackAddOutcome
{
    Added,

    /// <summary>Already in this stack: ignored.</summary>
    AlreadyScanned,

    /// <summary>Not an ISBN.</summary>
    Invalid,
}

/// <summary>
/// The queue behind "Scan a stack" (Phase 6e): ISBNs in the order scanned, each looked up once, with duplicates in
/// the stack ignored and books already in the library flagged. The dialog drives the lookups; this holds the state.
/// </summary>
public sealed class StackScan(Func<string, bool> isInLibrary)
{
    private readonly List<StackItem> _items = [];

    public IReadOnlyList<StackItem> Items => _items;

    public int Count => _items.Count;

    public int FoundCount => _items.Count(i => i.State == StackItemState.Found);

    /// <summary>Not found, or failed and not retried: these are filled in by hand afterwards.</summary>
    public IReadOnlyList<StackItem> Missing
        => _items.Where(i => i.State is StackItemState.NotFound or StackItemState.Failed).ToList();

    public bool IsLookingUp => _items.Any(i => i.State is StackItemState.Pending or StackItemState.LookingUp);

    /// <summary>Found books the user wants to add, in scan order.</summary>
    public IReadOnlyList<StackItem> ToAdd => _items.Where(i => i.State == StackItemState.Found && i.Include).ToList();

    /// <summary>Adds a scanned or typed ISBN (ISBN-10 or -13, any hyphens) to the end of the stack.</summary>
    public StackAddOutcome Add(string? input, out StackItem? item)
    {
        item = null;
        if (!Isbn.TryNormalize(input, out var isbn))
        {
            return StackAddOutcome.Invalid;
        }

        if (_items.Any(i => i.Isbn13 == isbn))
        {
            item = _items.First(i => i.Isbn13 == isbn);
            return StackAddOutcome.AlreadyScanned;
        }

        item = new StackItem(isbn, isInLibrary(isbn));
        _items.Add(item);
        return StackAddOutcome.Added;
    }

    public void Remove(StackItem item) => _items.Remove(item);

    /// <summary>The next ISBN to look up (oldest first), marked as in progress; null when none are waiting.</summary>
    public StackItem? StartNextLookup()
    {
        var next = _items.FirstOrDefault(i => i.State == StackItemState.Pending);
        if (next is not null)
        {
            next.State = StackItemState.LookingUp;
        }

        return next;
    }

    /// <summary>Records a lookup: a result is Found, null is NotFound.</summary>
    public static void Complete(StackItem item, BookLookupResult? result)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Result = result;
        item.State = result is null ? StackItemState.NotFound : StackItemState.Found;
    }

    public static void Fail(StackItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.State = StackItemState.Failed;
    }

    /// <summary>Puts a lookup back in the queue: after a failure, or one that was interrupted (went offline).</summary>
    public static void Retry(StackItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.State is StackItemState.Failed or StackItemState.LookingUp)
        {
            item.State = StackItemState.Pending;
        }
    }

    public void RetryFailed()
    {
        foreach (var item in _items.Where(i => i.State == StackItemState.Failed))
        {
            item.State = StackItemState.Pending;
        }
    }
}
