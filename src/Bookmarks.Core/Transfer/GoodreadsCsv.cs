using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Bookmarks.Core.Lookup;
using Bookmarks.Core.Storage;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Transfer;

/// <summary>One row of a Goodreads library export, cleaned up.</summary>
/// <param name="Title">Without the series suffix ("Dune" from "Dune (Dune, #1)").</param>
/// <param name="Rating">1–5, or null when the user didn't rate it (Goodreads writes 0).</param>
/// <param name="ExclusiveShelf">"read", "currently-reading", "to-read", or a custom exclusive shelf, lower case.</param>
public sealed record GoodreadsBook(
    string Title,
    string? Series,
    decimal? SeriesNumber,
    IReadOnlyList<string> Authors,
    string? Isbn13,
    int? Rating,
    string? Publisher,
    BookFormat? Format,
    int? PageCount,
    int? PublishedYear,
    DateOnly? DateRead,
    DateOnly? DateAdded,
    string ExclusiveShelf,
    int ReadCount,
    int OwnedCopies,
    string? Review,
    string? PrivateNotes)
{
    public const string Read = "read";
    public const string CurrentlyReading = "currently-reading";
    public const string ToRead = "to-read";

    /// <summary>Custom exclusive shelves people use for books they gave up on.</summary>
    public bool IsDidNotFinish => ExclusiveShelf is "did-not-finish" or "dnf" or "abandoned" or "gave-up";
}

/// <summary>The file isn't a Goodreads export. The message is meant for the user.</summary>
public sealed class GoodreadsFormatException : Exception
{
    public GoodreadsFormatException()
    {
    }

    public GoodreadsFormatException(string message)
        : base(message)
    {
    }

    public GoodreadsFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Goodreads' library CSV (My Books → Import and export): reading it for import, and writing it so the library can be
/// imported into Goodreads (or anything else that reads that format).
/// </summary>
public static partial class GoodreadsCsv
{
    /// <summary>Goodreads' own column order; exports use all of them so Goodreads' importer recognises the file.</summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        "Book Id", "Title", "Author", "Author l-f", "Additional Authors", "ISBN", "ISBN13", "My Rating",
        "Average Rating", "Publisher", "Binding", "Number of Pages", "Year Published", "Original Publication Year",
        "Date Read", "Date Added", "Bookshelves", "Bookshelves with positions", "Exclusive Shelf", "My Review",
        "Spoiler", "Private Notes", "Read Count", "Owned Copies",
    ];

    // Column limits on the server (BookmarksDbContext); longer values would make every sync push fail.
    private const int MaxTitle = 500;
    private const int MaxPublisher = 300;
    private const int MaxSeries = 300;
    private const int MaxAuthor = 200;

    /// <summary>"bookmarks-goodreads-2026-10-19.csv".</summary>
    public static string FileName(DateOnly date)
        => "bookmarks-goodreads-" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";

    // ---------- Import ----------

    /// <summary>Reads an export. Rows without a title are skipped. Throws <see cref="GoodreadsFormatException"/>.</summary>
    public static IReadOnlyList<GoodreadsBook> Parse(string csv)
    {
        var records = Csv.Parse(csv ?? "");
        if (records.Count == 0)
        {
            throw new GoodreadsFormatException("This file is empty.");
        }

        var header = records[0]
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.OrdinalIgnoreCase);

        if (!header.ContainsKey("Title") || !header.ContainsKey("Author"))
        {
            throw new GoodreadsFormatException(
                "This doesn't look like a Goodreads export. In Goodreads, go to My Books → Import and export → Export Library, and choose that file.");
        }

        var books = new List<GoodreadsBook>();
        foreach (var record in records.Skip(1))
        {
            string? Field(string name)
                => header.TryGetValue(name, out var index) && index < record.Length && !string.IsNullOrWhiteSpace(record[index])
                    ? record[index].Trim()
                    : null;

            var rawTitle = Field("Title");
            if (rawTitle is null)
            {
                continue;
            }

            var (title, series, number) = SplitSeries(rawTitle);
            var authors = new List<string>();
            AddNames(authors, Field("Author"));
            foreach (var name in (Field("Additional Authors") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                AddNames(authors, name);
            }

            var isbn13 = Isbn.TryNormalize(Unwrap(Field("ISBN13")), out var i13) ? i13
                : Isbn.TryNormalize(Unwrap(Field("ISBN")), out var i10) ? i10
                : null;

            var rating = Int(Field("My Rating"));
            books.Add(new GoodreadsBook(
                Clip(title, MaxTitle)!,
                Clip(series, MaxSeries),
                number,
                authors,
                isbn13,
                rating is >= 1 and <= 5 ? rating : null,
                Clip(Field("Publisher"), MaxPublisher),
                CatalogText.ParseFormat(Field("Binding")),
                Int(Field("Number of Pages")) is > 0 and var pages ? pages : null,
                YearOf(Field("Year Published")) ?? YearOf(Field("Original Publication Year")),
                Date(Field("Date Read")),
                Date(Field("Date Added")),
                (Field("Exclusive Shelf") ?? GoodreadsBook.ToRead).ToLowerInvariant(),
                Math.Max(0, Int(Field("Read Count")) ?? 0),
                Math.Max(0, Int(Field("Owned Copies")) ?? 0),
                PlainText(Field("My Review")),
                Field("Private Notes")));
        }

        return books;

        static void AddNames(List<string> authors, string? name)
        {
            var clean = name is null ? "" : Spaces().Replace(name, " ").Trim();
            if (clean.Length > 0 && !authors.Contains(clean, StringComparer.OrdinalIgnoreCase))
            {
                authors.Add(Clip(clean, MaxAuthor)!);
            }
        }
    }

    /// <summary>"Dune (Dune Chronicles, #1)" → ("Dune", "Dune Chronicles", 1). Titles without a "#n" suffix are left whole.</summary>
    public static (string Title, string? Series, decimal? Number) SplitSeries(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var m = SeriesSuffix().Match(title.Trim());
        if (!m.Success)
        {
            return (title.Trim(), null, null);
        }

        var number = decimal.TryParse(m.Groups["n"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : (decimal?)null;
        return (m.Groups["title"].Value.Trim(), m.Groups["series"].Value.Trim(), number);
    }

    /// <summary>Goodreads wraps ISBNs as <c>="0441172717"</c> so spreadsheets keep the leading zero.</summary>
    private static string? Unwrap(string? value)
        => value?.TrimStart('=').Trim('"').Trim() is { Length: > 0 } s ? s : null;

    private static int? Int(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static int? YearOf(string? value) => Int(value) is > 0 and < 10000 and var year ? year : null;

    private static readonly string[] DateFormats = ["yyyy/MM/dd", "yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d"];

    private static DateOnly? Date(string? value)
        => DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>Reviews are HTML-ish: line breaks as &lt;br/&gt;, entities escaped.</summary>
    private static string? PlainText(string? html)
    {
        if (html is null)
        {
            return null;
        }

        var text = LineBreak().Replace(html, "\n");
        text = Tag().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Trim();
        return text.Length > 0 ? text : null;
    }

    private static string? Clip(string? value, int max) => value is { } s && s.Length > max ? s[..max].TrimEnd() : value;

    // ---------- Export ----------

    /// <summary>The live library as a Goodreads CSV (header row first, \r\n line endings), one row per copy.</summary>
    public static async Task<string> ExportAsync(ILocalStore store, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var books = (await store.GetBooksAsync(ct)).Where(b => !b.IsDeleted).ToDictionary(b => b.Id);
        var items = (await store.GetAllAsync<LibraryItem>(ct: ct)).Where(i => books.ContainsKey(i.BookId));
        var sessions = (await store.GetAllAsync<ReadingSession>(ct: ct)).ToLookup(s => s.LibraryItemId);
        var notes = (await store.GetAllAsync<Note>(ct: ct)).ToLookup(n => n.LibraryItemId);

        var csv = new StringBuilder();
        csv.Append(Csv.FormatRecord(Columns)).Append("\r\n");
        foreach (var item in items.OrderBy(i => i.AddedAt))
        {
            csv.Append(Csv.FormatRecord(ExportRow(books[item.BookId], item, sessions[item.Id].ToList(), notes[item.Id].ToList())))
                .Append("\r\n");
        }

        return csv.ToString();
    }

    /// <summary>One row in <see cref="Columns"/> order.</summary>
    internal static string?[] ExportRow(Book book, LibraryItem item, IReadOnlyList<ReadingSession> sessions, IReadOnlyList<Note> notes)
    {
        var finished = sessions.Where(s => s.Outcome == ReadingOutcome.Finished).ToList();
        var reading = sessions.Any(s => s.Outcome == ReadingOutcome.InProgress);
        var shelf = reading ? GoodreadsBook.CurrentlyReading
            : finished.Count > 0 ? GoodreadsBook.Read
            : GoodreadsBook.ToRead;
        var shelves = item.Status == OwnershipStatus.Wishlist ? shelf + ", wishlist" : shelf;

        var title = book.Series is { Length: > 0 } series && book.SeriesNumber is { } number
            ? $"{book.Title} ({series}, #{number.ToString("0.##", CultureInfo.InvariantCulture)})"
            : book.Title;
        var author = book.Authors.Count > 0 ? book.Authors[0] : "";
        var isbn10 = book.Isbn13 is { } isbn ? Isbn.To10(isbn) : null;
        var rating = item.Rating is { } r ? (int)Math.Round(r, MidpointRounding.AwayFromZero) : 0;

        string?[] row =
        [
            "",                                                  // Book Id: Goodreads matches by ISBN / title
            title,
            author,
            LastFirst(author),
            string.Join(", ", book.Authors.Skip(1)),
            isbn10 is null ? "" : $"=\"{isbn10}\"",
            book.Isbn13 is null ? "" : $"=\"{book.Isbn13}\"",
            rating.ToString(CultureInfo.InvariantCulture),
            "",                                                  // Average Rating
            book.Publisher,
            Binding(item.Format),
            book.PageCount?.ToString(CultureInfo.InvariantCulture),
            book.PublishedYear?.ToString(CultureInfo.InvariantCulture),
            "",                                                  // Original Publication Year
            GoodreadsDate(finished.Max(s => s.FinishedOn)),
            GoodreadsDate(DateOnly.FromDateTime(item.AddedAt.UtcDateTime)),
            shelves,
            "",                                                  // Bookshelves with positions
            shelf,
            "",                                                  // My Review
            "",                                                  // Spoiler
            string.Join("\n\n", notes.OrderBy(n => n.CreatedAt).Select(n => n.Body.Trim()).Where(b => b.Length > 0)),
            finished.Count.ToString(CultureInfo.InvariantCulture),
            item.Status == OwnershipStatus.Wishlist ? "0" : "1",
        ];
        return row;
    }

    private static string? GoodreadsDate(DateOnly? date) => date?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    /// <summary>"Frank Herbert" → "Herbert, Frank".</summary>
    private static string LastFirst(string name)
    {
        var space = name.LastIndexOf(' ');
        return space <= 0 ? name : $"{name[(space + 1)..]}, {name[..space]}";
    }

    private static string Binding(BookFormat format) => format switch
    {
        BookFormat.Hardcover => "Hardcover",
        BookFormat.Paperback => "Paperback",
        BookFormat.MassMarketPaperback => "Mass Market Paperback",
        BookFormat.LeatherBound => "Leather Bound",
        _ => "",
    };

    [GeneratedRegex(@"^(?<title>.+?)\s*\((?<series>[^()]+?),?\s*#(?<n>\d+(?:\.\d+)?)(?:\s*-\s*\d+(?:\.\d+)?)?\)$")]
    private static partial Regex SeriesSuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();
}
