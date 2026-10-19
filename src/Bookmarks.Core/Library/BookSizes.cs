namespace Bookmarks.Core.Library;

/// <summary>A common trim size, for the editor's "Height" choice (Phase 6e).</summary>
/// <param name="Name">Shown in the list, e.g. "Octavo".</param>
/// <param name="Description">What it's typically used for.</param>
public sealed record BookSize(string Name, string Description, int HeightMm, int WidthMm);

public static class BookSizes
{
    /// <summary>Tallest first. Heights are what the shelf uses; widths are for reference.</summary>
    public static readonly IReadOnlyList<BookSize> Common =
    [
        new("Folio", "Art books and large-format volumes", 457, 305),
        new("Quarto", "Larger texts, atlases, coffee-table books", 305, 229),
        new("Octavo", "Most hardcover novels", 229, 152),
        new("A5", "Many paperbacks", 210, 148),
        new("B-format", "Common UK paperback", 198, 129),
        new("Mass-market paperback", "Small, pocket-size", 175, 108),
    ];

    /// <summary>The common size with exactly this height, or null.</summary>
    public static BookSize? WithHeight(int? heightMm)
        => heightMm is null ? null : Common.FirstOrDefault(s => s.HeightMm == heightMm);
}
