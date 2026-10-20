using Bookmarks.Shared.Domain;

namespace Bookmarks.Shared.Backup;

/// <summary>
/// A JSON backup of a library (Settings → Your data, Phase 7b): <c>bookmarks-backup-YYYY-MM-DD.json</c>.
/// Live records only (no tombstones), serialized with <c>BookmarksJsonContext</c> (camelCase, enums as strings).
/// Tags and presets aren't included (unused since Phase 6).
/// </summary>
public sealed class LibraryBackup
{
    /// <summary>Marks the file as ours, so a random JSON file is refused with a clear message.</summary>
    public const string FormatName = "bookmarks-backup";

    /// <summary>Bump when the shape changes in a way older restores can't read; restore refuses newer versions.</summary>
    public const int CurrentSchemaVersion = 1;

    public string Format { get; set; } = FormatName;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTimeOffset ExportedAt { get; set; }

    /// <summary>
    /// Whose library this was: the account's user id, or <see cref="Guid.Empty"/> for a signed-out library. Restoring into
    /// the same owner keeps record ids (so restoring twice doesn't duplicate); any other owner gets new ids.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>The app version that wrote the file (informational).</summary>
    public string? AppVersion { get; set; }

    public List<Book>? Books { get; set; } = [];

    public List<LibraryItem>? LibraryItems { get; set; } = [];

    public List<ReadingSession>? ReadingSessions { get; set; } = [];

    public List<Note>? Notes { get; set; } = [];

    public List<Bookcase>? Bookcases { get; set; } = [];

    public List<Shelf>? Shelves { get; set; } = [];

    public List<ShelfPlacement>? Placements { get; set; } = [];
}
