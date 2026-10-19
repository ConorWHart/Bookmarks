namespace Bookmarks.Core.Library;

/// <summary>A pile of books lying flat, first book on top. Width is the longest book; height is the pile's thickness.</summary>
public sealed record BookPile(IReadOnlyList<PlacedBook> Books, int WidthMm, int HeightMm);

/// <summary>
/// Stack view (Phase 6d): books lie flat in piles, spines facing out, in the view's sort order (top to bottom,
/// then pile to pile). Piles are then laid out in rows across the screen, like piles on a table.
/// </summary>
public static class StackLayoutEngine
{
    /// <summary>
    /// Cuts the books into piles in order. A pile ends when the next book would take it over
    /// <paramref name="maxHeightMm"/> or past <paramref name="maxBooks"/>; a single book always fits.
    /// </summary>
    /// <param name="minThicknessMm">Thin books are drawn at least this thick so they stay readable and tappable.</param>
    public static IReadOnlyList<BookPile> Piles(IEnumerable<LibraryEntry> entries, int maxHeightMm, int maxBooks, int minThicknessMm = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBooks, 1);

        var piles = new List<BookPile>();
        var current = new List<PlacedBook>();
        var height = 0;
        foreach (var entry in entries)
        {
            var thickness = Math.Max(minThicknessMm, SpineMetrics.ThicknessMm(entry));
            if (current.Count > 0 && (current.Count >= maxBooks || height + thickness > maxHeightMm))
            {
                piles.Add(Close(current, height));
                current = [];
                height = 0;
            }

            current.Add(new PlacedBook(entry, thickness, SpineMetrics.HeightMm(entry), FaceOut: false));
            height += thickness;
        }

        if (current.Count > 0)
        {
            piles.Add(Close(current, height));
        }

        return piles;
    }

    /// <summary>Fills rows left to right; a pile wider than the row gets a row of its own.</summary>
    public static IReadOnlyList<IReadOnlyList<BookPile>> Rows(IReadOnlyList<BookPile> piles, int rowWidthMm, int gapMm)
    {
        ArgumentNullException.ThrowIfNull(piles);

        var rows = new List<IReadOnlyList<BookPile>>();
        var current = new List<BookPile>();
        var used = 0;
        foreach (var pile in piles)
        {
            var needed = current.Count == 0 ? pile.WidthMm : used + gapMm + pile.WidthMm;
            if (current.Count > 0 && needed > rowWidthMm)
            {
                rows.Add(current);
                current = [];
                needed = pile.WidthMm;
            }

            current.Add(pile);
            used = needed;
        }

        if (current.Count > 0)
        {
            rows.Add(current);
        }

        return rows;
    }

    private static BookPile Close(List<PlacedBook> books, int height)
        => new(books, books.Max(b => b.HeightMm), height);
}
