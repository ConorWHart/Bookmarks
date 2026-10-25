using System.Text.Json;
using System.Text.Json.Serialization;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

/// <summary>
/// Converts a <see cref="LibraryView"/> to and from JSON. Used to remember the library's view on each
/// device (<c>pref:library.view</c>); kept as a versioned, flat DTO so it could also back saved presets later.
/// </summary>
/// <remarks>
/// <para>The JSON is a flat DTO, not the records themselves, so the in-memory model can change freely.
/// Rules for changing it: only <b>add</b> properties (with defaults that mean "no filter"); bump
/// <see cref="SchemaVersion"/> only when an old reading would be wrong, and convert old versions in
/// <see cref="FromJson"/>. Unknown properties are ignored; an unknown enum value makes the JSON unreadable
/// (null), and the caller falls back to the default view.</para>
/// <para>The search text is not saved: it's a transient "find a book" action.</para>
/// </remarks>
public static class LibraryViewJson
{
    public const int SchemaVersion = 1;

    public static string ToJson(LibraryView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return JsonSerializer.Serialize(ToDto(view), LibraryViewJsonContext.Default.LibraryViewDto);
    }

    /// <summary>Null when the JSON can't be read by this version of the app.</summary>
    public static LibraryView? FromJson(string? json, int schemaVersion = SchemaVersion)
    {
        if (string.IsNullOrWhiteSpace(json) || schemaVersion < 1)
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize(json, LibraryViewJsonContext.Default.LibraryViewDto);
            return dto is null ? null : FromDto(dto);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True when the two views show the same thing (ignores the search text and set order).</summary>
    public static bool SameView(LibraryView? a, LibraryView? b)
        => a is not null && b is not null && ToJson(a) == ToJson(b);

    private static LibraryViewDto ToDto(LibraryView view)
    {
        var f = view.Filter;
        return new LibraryViewDto
        {
            Display = view.Display,
            BooksLayout = view.BooksLayout,
            Sort = view.Query.Sort.Field,
            Descending = view.Query.Sort.Descending,
            GroupBy = view.Query.GroupBy,
            Statuses = Sorted(f.Statuses),
            Formats = Sorted(f.Formats),
            ReadState = f.ReadState,
            MinRating = f.MinRating,
            MinReadCount = f.MinReadCount,
            ShelfId = f.ShelfId,
            BookcaseId = f.BookcaseId,
            Authors = SortedText(f.Authors),
            Genres = SortedText(f.Genres),
            Series = SortedText(f.Series),
            Acquired = DateDto(f.Acquired),
            LastFinished = DateDto(f.LastFinished),
        };
    }

    private static LibraryView FromDto(LibraryViewDto d) => new()
    {
        Display = d.Display,
        BooksLayout = d.BooksLayout,
        Query = new LibraryQuery
        {
            Sort = new LibrarySort(d.Sort, d.Descending),
            GroupBy = d.GroupBy,
            Filter = new LibraryFilter
            {
                Statuses = SetOf(d.Statuses),
                Formats = SetOf(d.Formats),
                ReadState = d.ReadState,
                MinRating = d.MinRating,
                MinReadCount = d.MinReadCount,
                ShelfId = d.ShelfId,
                BookcaseId = d.BookcaseId,
                Authors = TextSetOf(d.Authors),
                Genres = TextSetOf(d.Genres),
                Series = TextSetOf(d.Series),
                Acquired = DateOf(d.Acquired),
                LastFinished = DateOf(d.LastFinished),
            },
        },
    };

    // Sets are written sorted so equal views always produce identical JSON (see SameView).
    private static List<T>? Sorted<T>(IReadOnlySet<T>? set)
        => set is { Count: > 0 } ? set.Order().ToList() : null;

    private static List<string>? SortedText(IReadOnlySet<string>? set)
        => set is { Count: > 0 } ? set.Order(StringComparer.OrdinalIgnoreCase).ToList() : null;

    private static HashSet<T>? SetOf<T>(List<T>? list)
        => list is { Count: > 0 } ? list.ToHashSet() : null;

    private static HashSet<string>? TextSetOf(List<string>? list)
        => list is { Count: > 0 } ? list.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;

    private static DateFilterDto? DateDto(DateFilter? filter)
        => filter is { IsActive: true } ? new DateFilterDto { Window = filter.Window, From = filter.From, To = filter.To } : null;

    private static DateFilter? DateOf(DateFilterDto? dto)
        => dto is null ? null : new DateFilter(dto.Window, dto.From, dto.To);
}

/// <summary>Schema 1 of the library view JSON. Add properties only; see <see cref="LibraryViewJson"/>.</summary>
internal sealed class LibraryViewDto
{
    public LibraryDisplay Display { get; set; }

    public BooksLayout BooksLayout { get; set; }

    public LibrarySortField Sort { get; set; } = LibrarySortField.Author;

    public bool Descending { get; set; }

    public LibraryGroupBy GroupBy { get; set; }

    public List<OwnershipStatus>? Statuses { get; set; }

    public List<BookFormat>? Formats { get; set; }

    public ReadState ReadState { get; set; }

    public decimal? MinRating { get; set; }

    public int? MinReadCount { get; set; }

    public Guid? ShelfId { get; set; }

    public Guid? BookcaseId { get; set; }

    public List<string>? Authors { get; set; }

    public List<string>? Genres { get; set; }

    public List<string>? Series { get; set; }

    public DateFilterDto? Acquired { get; set; }

    public DateFilterDto? LastFinished { get; set; }
}

internal sealed class DateFilterDto
{
    public DateWindow Window { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}

/// <remarks>
/// Nulls are omitted to keep presets small. Defaults are always written (never WhenWritingDefault):
/// some DTO defaults differ from the type default, e.g. <see cref="LibraryViewDto.Sort"/>.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LibraryViewDto))]
internal sealed partial class LibraryViewJsonContext : JsonSerializerContext
{
}
