using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

/// <summary>
/// Estimates a book's physical spine size. Catalog APIs rarely provide dimensions, so we fall
/// back to page count (thickness) and typical trim sizes per format (height).
/// </summary>
public static class SpineMetrics
{
    // Typical paper is ~0.06–0.1 mm per page (a "page" here is one side, i.e. half a leaf). Heavier paper
    // (illustrated books, art paper) is noticeably thicker.
    private const double MmPerPageVeryThin = 0.035;
    private const double MmPerPageThin = 0.05;
    private const double MmPerPage = 0.065;
    private const double MmPerPageThick = 0.09;
    private const double MmPerPageVeryThick = 0.12;
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

        // Automatic: a measured thickness when the book has one. A chosen paper thickness overrides it.
        var paper = e.Item.Appearance.PaperThickness;
        if (paper is null && e.Book.ThicknessMm is { } t)
        {
            return t;
        }

        var pages = e.Book.PageCount ?? DefaultPageCount;
        var cover = e.Item.Format switch
        {
            BookFormat.Hardcover or BookFormat.LeatherBound => 6,
            _ => 1,
        };

        var perPage = paper switch
        {
            PaperThickness.VeryThin => MmPerPageVeryThin,
            PaperThickness.Thin => MmPerPageThin,
            PaperThickness.Thick => MmPerPageThick,
            PaperThickness.VeryThick => MmPerPageVeryThick,
            _ => MmPerPage,
        };

        return Math.Clamp((int)Math.Round((pages * perPage) + cover), MinThicknessMm, MaxThicknessMm);
    }

    public static int HeightMm(LibraryEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.Item.Appearance.HeightMmOverride ?? NaturalHeightMm(e.Book.HeightMm, e.Item.Format);
    }

    /// <summary>Height without this copy's override: the book's measured height, else a typical one for the format.</summary>
    public static int NaturalHeightMm(int? bookHeightMm, BookFormat format)
        => bookHeightMm
            ?? format switch
            {
                BookFormat.Hardcover => 240,
                BookFormat.LeatherBound => 240,
                BookFormat.MassMarketPaperback => 175,
                _ => 210,
            };
}
