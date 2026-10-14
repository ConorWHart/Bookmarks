using System.Security.Cryptography;
using Bookmarks.Core.Ranking;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

/// <summary>
/// The one-time move (Phase 6b) from the legacy single shelf on <see cref="LibraryItem.ShelfId"/> to
/// <see cref="ShelfPlacement"/> rows. Each device runs it once; the ids are derived from (item, shelf), so two
/// devices doing it write the same rows, and a placement the user has since removed (a deleted row with that id)
/// is never brought back.
/// </summary>
public static class PlacementMigration
{
    /// <summary>Meta key set once the move has run on this database.</summary>
    public const string DoneKey = "migr:placements";

    /// <param name="items">Every library item, deleted ones included (an undone delete keeps its place).</param>
    /// <param name="liveShelves">Shelves that exist (not deleted) in bookcases that exist.</param>
    /// <param name="existing">Every placement, deleted ones included.</param>
    /// <returns>The placements to create.</returns>
    public static IReadOnlyList<ShelfPlacement> Plan(
        IEnumerable<LibraryItem> items,
        IEnumerable<Shelf> liveShelves,
        IEnumerable<ShelfPlacement> existing)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(liveShelves);
        ArgumentNullException.ThrowIfNull(existing);

        var bookcaseOf = liveShelves.ToDictionary(s => s.Id, s => s.BookcaseId);
        var all = existing.ToList();
        var knownIds = all.Select(p => p.Id).ToHashSet();

        // A book already placed in a bookcase (e.g. by a newer version on another device) isn't placed there again.
        var taken = all
            .Where(p => !p.IsDeleted && bookcaseOf.ContainsKey(p.ShelfId))
            .Select(p => (p.LibraryItemId, bookcaseOf[p.ShelfId]))
            .ToHashSet();

        var result = new List<ShelfPlacement>();
        foreach (var item in items)
        {
            if (item.ShelfId is not { } shelfId || !bookcaseOf.TryGetValue(shelfId, out var bookcaseId))
            {
                continue;
            }

            var id = IdFor(item.Id, shelfId);
            if (knownIds.Contains(id) || !taken.Add((item.Id, bookcaseId)))
            {
                continue;
            }

            result.Add(new ShelfPlacement
            {
                Id = id,
                UserId = item.UserId,
                ShelfId = shelfId,
                LibraryItemId = item.Id,
                Rank = string.IsNullOrEmpty(item.ShelfRank) ? RankKey.Between(null, null) : item.ShelfRank,
            });
        }

        return result;
    }

    /// <summary>A stable id for the moved placement of <paramref name="itemId"/> on <paramref name="shelfId"/> (UUID version 8).</summary>
    public static Guid IdFor(Guid itemId, Guid shelfId)
    {
        Span<byte> input = stackalloc byte[32];
        itemId.TryWriteBytes(input[..16], bigEndian: true, out _);
        shelfId.TryWriteBytes(input[16..], bigEndian: true, out _);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);

        Span<byte> bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8: custom
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }
}
