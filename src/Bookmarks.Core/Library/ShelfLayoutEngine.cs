namespace Bookmarks.Core.Library;

public sealed record PlacedBook(LibraryEntry Entry, int WidthMm, int HeightMm, bool FaceOut);

public sealed record ShelfRow(IReadOnlyList<PlacedBook> Books, int UsedWidthMm);

/// <summary>
/// "Generated mode": flows an ordered list of books onto virtual shelves of a given width,
/// like text wrapping. Physical mode doesn't use this; books stay on their assigned shelf.
/// </summary>
public static class ShelfLayoutEngine
{
    /// <param name="entries">Already filtered and sorted.</param>
    /// <param name="shelfWidthMm">Usable width of one shelf in real-world millimetres.</param>
    /// <param name="gapMm">Spacing between books.</param>
    public static IReadOnlyList<ShelfRow> Flow(IEnumerable<LibraryEntry> entries, int shelfWidthMm, int gapMm = 1)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shelfWidthMm);

        var rows = new List<ShelfRow>();
        var current = new List<PlacedBook>();
        var used = 0;

        foreach (var e in entries)
        {
            var faceOut = e.Item.Appearance.FaceOut;
            var height = SpineMetrics.HeightMm(e);

            // Face-out books show their cover, so they take the cover's width (≈ 2/3 of height).
            var width = faceOut
                ? e.Book.WidthMm ?? (int)Math.Round(height * 0.66)
                : SpineMetrics.ThicknessMm(e);

            var needed = width + (current.Count > 0 ? gapMm : 0);
            if (current.Count > 0 && used + needed > shelfWidthMm)
            {
                rows.Add(new ShelfRow(current, used));
                current = [];
                used = 0;
                needed = width;
            }

            current.Add(new PlacedBook(e, width, height, faceOut));
            used += needed;
        }

        if (current.Count > 0)
        {
            rows.Add(new ShelfRow(current, used));
        }

        return rows;
    }
}
