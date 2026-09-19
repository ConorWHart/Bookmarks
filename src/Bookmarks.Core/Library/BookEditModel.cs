using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

/// <summary>What the user says about their reading when adding a book.</summary>
public enum InitialReadState
{
    Unread,
    Reading,
    Read,
}

/// <summary>Spine style choice in the editor; <see cref="Automatic"/> = derive from format.</summary>
public enum SpineStyleChoice
{
    Automatic,
    Classic,
    Banded,
    Minimal,
    Ornate,
}

/// <summary>
/// Form model for the add/edit book dialog. The dialog edits this copy, so cancelling never
/// touches the stored entities; <see cref="ApplyTo"/> writes the result onto a Book + LibraryItem.
/// </summary>
public sealed class BookEditModel
{
    /// <summary>Cloth-like spine colors offered as swatches; new books get one at random.</summary>
    public static readonly IReadOnlyList<string> SpinePalette =
    [
        "#8C2F1B", "#B5452F", "#C1121F", "#6E2C2C", "#D9822B", "#C9A227", "#F2C14E", "#4A5D23",
        "#2E5E4E", "#3B7EA1", "#1F3A5F", "#274060", "#5B3F8C", "#E8E4D8", "#8E8E8E", "#111111",
    ];

    /// <summary>Lettering colors: white, cream, gilt, silver, black, dark brown, oxblood, navy.</summary>
    public static readonly IReadOnlyList<string> TextPalette =
    [
        "#FFFFFF", "#F4EBD9", "#E9C46A", "#C0C0C0", "#111111", "#3B2A1E", "#6E1E1E", "#1F3A5F",
    ];

    private decimal? _originalRating;
    private int _originalStars;

    public string? Isbn13 { get; set; }

    public string Title { get; set; } = "";

    public string? Subtitle { get; set; }

    /// <summary>Comma-separated.</summary>
    public string? AuthorsText { get; set; }

    public string? Publisher { get; set; }

    public int? PublishedYear { get; set; }

    public int? PageCount { get; set; }

    /// <summary>Comma-separated.</summary>
    public string? GenresText { get; set; }

    public string? Series { get; set; }

    public decimal? SeriesNumber { get; set; }

    public OwnershipStatus Status { get; set; } = OwnershipStatus.Owned;

    public BookFormat Format { get; set; } = BookFormat.Paperback;

    public string? LentTo { get; set; }

    /// <summary>Null = Unshelved.</summary>
    public Guid? ShelfId { get; set; }

    public bool WantToReread { get; set; }

    /// <summary>0 = not rated. Whole stars in the editor; half-star ratings are kept unless changed.</summary>
    public int RatingStars { get; set; }

    /// <summary>Effective spine color (#RRGGBB).</summary>
    public string? SpineColor { get; set; }

    /// <summary>Spine lettering color (#RRGGBB); null = automatic (contrast, or gilt on ornate).</summary>
    public string? TextColor { get; set; }

    public SpineStyleChoice Style { get; set; }

    public bool FaceOut { get; set; }

    /// <summary>Only used when adding.</summary>
    public InitialReadState ReadState { get; set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Title);

    public static BookEditModel ForNew(Guid? shelfId = null, Random? random = null)
        => new()
        {
            ShelfId = shelfId,
            SpineColor = SpinePalette[(random ?? Random.Shared).Next(SpinePalette.Count)],
        };

    public static BookEditModel FromEntry(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var (book, item) = (entry.Book, entry.Item);
        var stars = ToStars(item.Rating);

        return new BookEditModel
        {
            Isbn13 = book.Isbn13,
            Title = book.Title,
            Subtitle = book.Subtitle,
            AuthorsText = string.Join(", ", book.Authors),
            Publisher = book.Publisher,
            PublishedYear = book.PublishedYear,
            PageCount = book.PageCount,
            GenresText = string.Join(", ", book.Genres),
            Series = book.Series,
            SeriesNumber = book.SeriesNumber,
            Status = item.Status,
            Format = item.Format,
            LentTo = item.LentTo,
            ShelfId = item.ShelfId,
            WantToReread = item.WantToReread,
            RatingStars = stars,
            SpineColor = entry.SpineColor,
            TextColor = item.Appearance.TextColor,
            Style = ToChoice(item.Appearance.Style),
            FaceOut = item.Appearance.FaceOut,
            _originalRating = item.Rating,
            _originalStars = stars,
        };
    }

    /// <summary>Writes the form onto the entities. Does not touch identity, placement rank or sync fields.</summary>
    public void ApplyTo(Book book, LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(item);

        book.Isbn13 = Clean(Isbn13)?.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        book.Title = Title.Trim();
        book.Subtitle = Clean(Subtitle);
        book.Authors = SplitList(AuthorsText).ToList();
        book.Publisher = Clean(Publisher);
        book.PublishedYear = PublishedYear;
        book.PageCount = PageCount is > 0 ? PageCount : null;
        book.Genres = SplitList(GenresText).ToList();
        book.Series = Clean(Series);
        book.SeriesNumber = book.Series is null ? null : SeriesNumber;

        item.Status = Status;
        item.Format = Format;
        item.LentTo = Status == OwnershipStatus.LentOut ? Clean(LentTo) : null;
        item.ShelfId = ShelfId;
        item.WantToReread = WantToReread;
        item.Rating = RatingStars == _originalStars ? _originalRating
            : RatingStars == 0 ? null
            : (decimal?)RatingStars;

        // Only store a spine color override when it differs from the catalog (cover) color.
        var color = Clean(SpineColor)?.ToUpperInvariant();
        item.Appearance.SpineColor = string.Equals(color, book.DominantColor, StringComparison.OrdinalIgnoreCase) ? null : color;
        item.Appearance.TextColor = Clean(TextColor)?.ToUpperInvariant();
        item.Appearance.Style = ToStyle(Style);
        item.Appearance.FaceOut = FaceOut;
    }

    /// <summary>Splits "a, b,,c " into ["a", "b", "c"], dropping duplicates (case-insensitive).</summary>
    public static IReadOnlyList<string> SplitList(string? text)
        => (text ?? "")
            .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static SpineStyleChoice ToChoice(SpineStyle? style) => style switch
    {
        SpineStyle.Classic => SpineStyleChoice.Classic,
        SpineStyle.Banded => SpineStyleChoice.Banded,
        SpineStyle.Minimal => SpineStyleChoice.Minimal,
        SpineStyle.Ornate => SpineStyleChoice.Ornate,
        _ => SpineStyleChoice.Automatic,
    };

    private static SpineStyle? ToStyle(SpineStyleChoice choice) => choice switch
    {
        SpineStyleChoice.Classic => SpineStyle.Classic,
        SpineStyleChoice.Banded => SpineStyle.Banded,
        SpineStyleChoice.Minimal => SpineStyle.Minimal,
        SpineStyleChoice.Ornate => SpineStyle.Ornate,
        _ => null,
    };

    private static int ToStars(decimal? rating)
        => rating is { } r ? (int)Math.Round(r, MidpointRounding.AwayFromZero) : 0;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
