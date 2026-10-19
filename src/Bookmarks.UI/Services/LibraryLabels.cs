using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;

namespace Bookmarks.UI.Services;

/// <summary>Display labels for library view options, shared by the toolbar and the filter panel.</summary>
public static class LibraryLabels
{
    public static readonly IReadOnlyList<(LibrarySortField Field, string Label)> SortOptions =
    [
        (LibrarySortField.Author, "Author"),
        (LibrarySortField.Title, "Title"),
        (LibrarySortField.Series, "Series"),
        (LibrarySortField.Color, "Color (rainbow)"),
        (LibrarySortField.Height, "Height"),
        (LibrarySortField.DateAdded, "Date added"),
        (LibrarySortField.Rating, "Rating"),
        (LibrarySortField.ReadCount, "Times read"),
        (LibrarySortField.LastRead, "Last read"),
    ];

    public static readonly IReadOnlyList<(LibraryGroupBy Group, string Label)> GroupOptions =
    [
        (LibraryGroupBy.None, "None"),
        (LibraryGroupBy.Author, "Author"),
        (LibraryGroupBy.Genre, "Genre"),
        (LibraryGroupBy.Series, "Series"),
        (LibraryGroupBy.Status, "Status"),
        (LibraryGroupBy.Format, "Format"),
    ];

    public static readonly IReadOnlyList<(BookFormat Format, string Label)> Formats =
    [
        (BookFormat.Hardcover, "Hardcover"),
        (BookFormat.Paperback, "Paperback"),
        (BookFormat.MassMarketPaperback, "Mass market"),
        (BookFormat.LeatherBound, "Leather"),
        (BookFormat.Other, "Other"),
    ];

    public static readonly IReadOnlyList<(OwnershipStatus Status, string Label)> Statuses =
    [
        (OwnershipStatus.Owned, "Owned"),
        (OwnershipStatus.Wishlist, "Wishlist"),
        (OwnershipStatus.LentOut, "Lent out"),
    ];
}
