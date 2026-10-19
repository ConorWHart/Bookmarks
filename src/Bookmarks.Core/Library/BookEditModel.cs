using Bookmarks.Core.Lookup;
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
    private string? _spineColor;

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

    /// <summary>Cover image (medium size), usually from a lookup.</summary>
    public string? CoverUrl { get; set; }

    /// <summary>Dominant color of <see cref="CoverUrl"/> (#RRGGBB); set through <see cref="UseCoverColor"/>.</summary>
    public string? DominantColor { get; set; }

    public int? HeightMm { get; set; }

    public int? WidthMm { get; set; }

    public int? ThicknessMm { get; set; }

    public BookSource Source { get; set; }

    public string? Series { get; set; }

    public decimal? SeriesNumber { get; set; }

    public OwnershipStatus Status { get; set; } = OwnershipStatus.Owned;

    public BookFormat Format { get; set; } = BookFormat.Paperback;

    public string? LentTo { get; set; }

    public bool WantToReread { get; set; }

    /// <summary>0 = not rated. Whole stars in the editor; half-star ratings are kept unless changed.</summary>
    public int RatingStars { get; set; }

    /// <summary>Effective spine color (#RRGGBB). Setting it (the color picker) stops it following the cover.</summary>
    public string? SpineColor
    {
        get => _spineColor;
        set
        {
            _spineColor = value;
            SpineColorFollowsCover = false;
        }
    }

    /// <summary>
    /// True while the user hasn't chosen a spine color (a new book's random swatch, or a book with no override),
    /// so a newly extracted cover color becomes the spine color. Set after <see cref="SpineColor"/> in initializers.
    /// </summary>
    public bool SpineColorFollowsCover { get; set; }

    /// <summary>Spine lettering color (#RRGGBB); null = automatic (contrast, or gilt on ornate).</summary>
    public string? TextColor { get; set; }

    public SpineStyleChoice Style { get; set; }

    public bool FaceOut { get; set; }

    /// <summary>This copy's paper (with the page count, how thick the spine looks); null = automatic.</summary>
    public PaperThickness? PaperThickness { get; set; }

    /// <summary>This copy's height on the shelf (mm); null = automatic (the book's height, else by format).</summary>
    public int? HeightOverrideMm { get; set; }

    /// <summary>Only used when adding.</summary>
    public InitialReadState ReadState { get; set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Title);

    /// <remarks>Shelf placement isn't part of the editor (Phase 6b): books are put on shelves separately.</remarks>
    public static BookEditModel ForNew(Random? random = null)
        => new()
        {
            SpineColor = SpinePalette[(random ?? Random.Shared).Next(SpinePalette.Count)],
            SpineColorFollowsCover = true,
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
            CoverUrl = book.CoverUrl,
            DominantColor = book.DominantColor,
            HeightMm = book.HeightMm,
            WidthMm = book.WidthMm,
            ThicknessMm = book.ThicknessMm,
            Source = book.Source,
            Series = book.Series,
            SeriesNumber = book.SeriesNumber,
            Status = item.Status,
            Format = item.Format,
            LentTo = item.LentTo,
            WantToReread = item.WantToReread,
            RatingStars = stars,
            SpineColor = entry.SpineColor,
            SpineColorFollowsCover = item.Appearance.SpineColor is null,
            TextColor = item.Appearance.TextColor,
            Style = ToChoice(item.Appearance.Style),
            FaceOut = item.Appearance.FaceOut,
            PaperThickness = item.Appearance.PaperThickness,
            HeightOverrideMm = item.Appearance.HeightMmOverride,
            _originalRating = item.Rating,
            _originalStars = stars,
        };
    }

    /// <summary>Writes the form onto the entities. Does not touch identity, placement rank or sync fields.</summary>
    public void ApplyTo(Book book, LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(item);

        book.Isbn13 = Isbn.TryNormalize(Isbn13, out var isbn13)
            ? isbn13
            : Clean(Isbn13)?.Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        book.Title = Title.Trim();
        book.Subtitle = Clean(Subtitle);
        book.Authors = SplitList(AuthorsText).ToList();
        book.Publisher = Clean(Publisher);
        book.PublishedYear = PublishedYear;
        book.PageCount = PageCount is > 0 ? PageCount : null;
        book.Genres = SplitList(GenresText).ToList();
        book.Series = Clean(Series);
        book.SeriesNumber = book.Series is null ? null : SeriesNumber;
        book.CoverUrl = Clean(CoverUrl);
        book.DominantColor = Clean(DominantColor)?.ToUpperInvariant();
        book.HeightMm = HeightMm is > 0 ? HeightMm : null;
        book.WidthMm = WidthMm is > 0 ? WidthMm : null;
        book.ThicknessMm = ThicknessMm is > 0 ? ThicknessMm : null;
        book.Source = Source;

        item.Status = Status;
        item.Format = Format;
        item.LentTo = Status == OwnershipStatus.LentOut ? Clean(LentTo) : null;
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
        item.Appearance.PaperThickness = PaperThickness;
        item.Appearance.HeightMmOverride = HeightOverrideMm is > 0 ? HeightOverrideMm : null;
    }

    /// <summary>
    /// Records the color extracted from the cover. The spine takes it too unless the user has chosen a spine color;
    /// <see cref="ApplyTo"/> then stores no override, so the spine keeps following the book's cover color.
    /// </summary>
    public void UseCoverColor(string? hex)
    {
        DominantColor = Clean(hex)?.ToUpperInvariant();
        if (SpineColorFollowsCover && DominantColor is not null)
        {
            _spineColor = DominantColor;
            EnsureReadableText();
        }
    }

    /// <summary>
    /// After the spine color changes on its own (a cover color), drops a chosen text color that would be hard to read
    /// on it, so the lettering goes back to automatic (black or white by contrast).
    /// </summary>
    public void EnsureReadableText()
    {
        if (TextColor is { } text && SpineColor is { } spine && ColorUtil.ContrastRatio(text, spine) < ColorUtil.ComfortableContrast)
        {
            TextColor = null;
        }
    }

    /// <summary>Removes the cover; the spine keeps its current color (stored as an override from then on).</summary>
    public void RemoveCover()
    {
        CoverUrl = null;
        DominantColor = null;
        SpineColorFollowsCover = false;
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
