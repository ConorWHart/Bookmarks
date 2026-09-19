using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

/// <summary>
/// Estimates a book's physical spine size. Catalog APIs rarely provide dimensions, so we fall
/// back to page count (thickness) and typical trim sizes per format (height).
/// </summary>
public static class SpineMetrics
{
    // Typical paper is ~0.06–0.1 mm per page (a "page" here is one side, i.e. half a leaf).
    private const double MmPerPage = 0.065;
    private const int DefaultPageCount = 300;
    private const int MinThicknessMm = 6;
    private const int MaxThicknessMm = 90;

    public static int ThicknessMm(LibraryEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Item.Appearance.ThicknessMmOverride is { } o)
        {
            return o;
        }

        if (e.Book.ThicknessMm is { } t)
        {
            return t;
        }

        var pages = e.Book.PageCount ?? DefaultPageCount;
        var cover = e.Item.Format switch
        {
            BookFormat.Hardcover or BookFormat.LeatherBound => 6,
            _ => 1,
        };

        return Math.Clamp((int)Math.Round((pages * MmPerPage) + cover), MinThicknessMm, MaxThicknessMm);
    }

    public static int HeightMm(LibraryEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.Item.Appearance.HeightMmOverride
            ?? e.Book.HeightMm
            ?? e.Item.Format switch
            {
                BookFormat.Hardcover => 240,
                BookFormat.LeatherBound => 240,
                BookFormat.MassMarketPaperback => 175,
                _ => 210,
            };
    }
}
