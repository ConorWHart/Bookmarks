using Bookmarks.Shared.Sync;

namespace Bookmarks.Shared.Domain;

/// <summary>A user's copy of (or wish for) a <see cref="Book"/>.</summary>
public sealed class LibraryItem : SyncEntity
{
    public Guid BookId { get; set; }

    public OwnershipStatus Status { get; set; }

    public BookFormat Format { get; set; }

    public BookCondition? Condition { get; set; }

    /// <summary>
    /// Legacy (Phases 2–6a): the one shelf the book stood on. Replaced by <see cref="ShelfPlacement"/> in
    /// Phase 6b and moved into placements once per device; no longer read. Kept so older app versions can
    /// still sync; drop in Phase 7.
    /// </summary>
    public Guid? ShelfId { get; set; }

    /// <summary>Legacy, see <see cref="ShelfId"/>.</summary>
    public string? ShelfRank { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateOnly? AcquiredOn { get; set; }

    public decimal? PurchasePrice { get; set; }

    public string? LentTo { get; set; }

    public bool WantToReread { get; set; }

    /// <summary>0.5 – 5 in half steps.</summary>
    public decimal? Rating { get; set; }

    public List<Guid> TagIds { get; set; } = [];

    public SpineAppearance Appearance { get; set; } = new();
}

/// <summary>Per-copy overrides for how the book renders on the shelf.</summary>
public sealed class SpineAppearance
{
    public string? SpineColor { get; set; }

    public string? TextColor { get; set; }

    public SpineStyle? Style { get; set; }

    public string? SpineImageUrl { get; set; }

    public bool FaceOut { get; set; }

    public int? HeightMmOverride { get; set; }

    public int? ThicknessMmOverride { get; set; }
}

public sealed class ReadingSession : SyncEntity
{
    public Guid LibraryItemId { get; set; }

    public DateOnly? StartedOn { get; set; }

    public DateOnly? FinishedOn { get; set; }

    public ReadingOutcome Outcome { get; set; }

    public decimal? Rating { get; set; }
}

public sealed class Note : SyncEntity
{
    public Guid LibraryItemId { get; set; }

    public string Body { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Tag : SyncEntity
{
    public string Name { get; set; } = "";

    public string? Color { get; set; }
}

public sealed class Bookcase : SyncEntity
{
    public string Name { get; set; } = "";

    /// <summary>Theme key, e.g. "oak", "walnut", "white", "dark".</summary>
    public string Theme { get; set; } = "oak";

    public string? SortRank { get; set; }

    /// <summary>Not drawn in the Bookcases view (its books still show in Books and search). Synced.</summary>
    public bool IsHidden { get; set; }
}

/// <summary>
/// A book on a shelf. A book can be in any number of bookcases, but at most once per bookcase.
/// Removing it from the shelf soft-deletes this row; the book stays in the library.
/// </summary>
/// <remarks>
/// Rows are created offline and can arrive in any order, so there are no foreign keys. Two offline devices
/// can place the same book in the same bookcase; readers keep the lowest id and the next placement edit
/// of that book deletes the extras.
/// </remarks>
public sealed class ShelfPlacement : SyncEntity
{
    public Guid ShelfId { get; set; }

    public Guid LibraryItemId { get; set; }

    /// <summary>Fractional rank key for order on the shelf (see <c>RankKey</c>).</summary>
    public string? Rank { get; set; }
}

public sealed class Shelf : SyncEntity
{
    public Guid BookcaseId { get; set; }

    public string? Name { get; set; }

    public string? SortRank { get; set; }

    /// <summary>Usable width, used to show fullness / capacity.</summary>
    public int? WidthMm { get; set; }
}

/// <summary>
/// Saved filter/sort/grouping or style preset. The JSON shape is owned by the client
/// (see <c>Bookmarks.Core.Library</c>); the server stores it opaquely.
/// </summary>
public sealed class Preset : SyncEntity
{
    public PresetKind Kind { get; set; }

    public string Name { get; set; } = "";

    public bool IsDefault { get; set; }

    public int SchemaVersion { get; set; } = 1;

    public string Json { get; set; } = "{}";
}

public enum PresetKind
{
    Filter = 0,
    Style = 1,
}
