using System.Globalization;
using System.Net;
using System.Text.Json;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Lookup;

/// <summary>
/// Book lookup against Open Library, called straight from the browser (its API and covers allow CORS).
/// </summary>
/// <remarks>
/// <para>An ISBN lookup makes two requests in parallel:</para>
/// <list type="bullet">
/// <item><c>/api/books?jscmd=data</c>: title, author <b>names</b> (resolved from the work when the edition has none),
/// publisher, date, pages, subjects and cover URLs.</item>
/// <item><c>/isbn/{isbn}.json</c>: the raw edition, for what the first leaves out (binding, physical dimensions,
/// subtitle, cover ids). Optional: if it fails, the lookup still succeeds.</item>
/// </list>
/// <para>Many editions have no cover or subjects of their own; the work (the book across all its editions) usually
/// does. Only then is a third request made, for <c>/works/{id}.json</c>, to borrow its main cover and subjects.</para>
/// <para>Responses are read with <see cref="JsonDocument"/> rather than typed models: the records are user-edited
/// and inconsistent (publishers as strings or objects, numbers as strings, missing fields), and this stays trim-safe.
/// Open Library asks for ≤ 1 request per second for unidentified clients; bulk callers must pace themselves.</para>
/// </remarks>
public sealed class OpenLibraryLookup(HttpClient http) : IBookLookup
{
    public static readonly Uri DefaultBaseAddress = new("https://openlibrary.org/");

    private const string SearchFields =
        "key,title,subtitle,author_name,first_publish_year,cover_i,edition_count,number_of_pages_median,subject,"
        + "editions,editions.key,editions.title,editions.isbn,editions.cover_i,editions.publisher,editions.number_of_pages";

    /// <summary>Search results prefer editions in this language (ISO 639-1).</summary>
    public string Language { get; init; } = "en";

    public async Task<BookLookupResult?> FindByIsbnAsync(string isbn, CancellationToken ct = default)
    {
        if (!Isbn.TryNormalize(isbn, out var isbn13))
        {
            throw new ArgumentException("Not a valid ISBN.", nameof(isbn));
        }

        var editionTask = GetEditionAsync(isbn13, ct);
        using var data = await GetJsonAsync($"api/books?bibkeys=ISBN:{isbn13}&jscmd=data&format=json", ct).ConfigureAwait(false);
        using var edition = await editionTask.ConfigureAwait(false);

        var result = ParseIsbnLookup(isbn13, data?.RootElement, edition?.RootElement);
        if (result is not null && (result.CoverUrl is null || result.Genres.Count == 0) && WorkKey(edition?.RootElement) is { } workKey)
        {
            result = await AddWorkDetailsAsync(result, workKey, ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Fills a missing cover and genres from the work record. Failures leave the result as it was.</summary>
    private async Task<BookLookupResult> AddWorkDetailsAsync(BookLookupResult result, string workKey, CancellationToken ct)
    {
        try
        {
            using var work = await GetJsonAsync(workKey.TrimStart('/') + ".json", ct).ConfigureAwait(false);
            return work is null ? result : MergeWork(result, work.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return result;
        }
    }

    internal static BookLookupResult MergeWork(BookLookupResult result, JsonElement work)
    {
        var cover = result.CoverUrl
            ?? (FirstCoverId(Arr(work, "covers")) is { } id ? CoverUrls.ForOpenLibraryId(id) : null);
        var genres = result.Genres.Count > 0 ? result.Genres : GenreMapper.FromSubjects(Strings(Arr(work, "subjects")));
        return result with { CoverUrl = cover, Genres = genres };
    }

    /// <summary>The edition's work, e.g. "/works/OL18139176W"; only well-formed keys, since it becomes part of a URL.</summary>
    internal static string? WorkKey(JsonElement? edition)
    {
        if (Arr(edition, "works") is not { } works)
        {
            return null;
        }

        foreach (var w in works.EnumerateArray())
        {
            var key = Str(w, "key");
            if (key is not null && key.StartsWith("/works/OL", StringComparison.Ordinal) && key.EndsWith('W')
                && key[9..^1].All(char.IsAsciiDigit) && key.Length > 10)
            {
                return key;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<BookSearchHit>> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // An ISBN typed into the search box is an ISBN lookup.
        if (Isbn.TryNormalize(query, out var isbn13))
        {
            var hit = await FindByIsbnAsync(isbn13, ct).ConfigureAwait(false);
            return hit is null ? [] : [ToHit(hit)];
        }

        var url = string.Create(CultureInfo.InvariantCulture,
            $"search.json?q={Uri.EscapeDataString(query.Trim())}&fields={SearchFields}&limit={Math.Clamp(limit, 1, 50)}&lang={Uri.EscapeDataString(Language)}");
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
        return doc is null ? [] : ParseSearch(doc.RootElement);
    }

    internal static BookLookupResult? ParseIsbnLookup(string isbn13, JsonElement? dataRoot, JsonElement? edition)
    {
        // jscmd=data answers { "ISBN:978…": { … } }, or {} when Open Library doesn't know the ISBN.
        JsonElement? data = null;
        if (dataRoot is { ValueKind: JsonValueKind.Object } root)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Object)
                {
                    data = p.Value;
                    break;
                }
            }
        }

        if (edition is { ValueKind: not JsonValueKind.Object })
        {
            edition = null;
        }

        var title = Str(data, "title") ?? Str(edition, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var subtitle = Str(data, "subtitle") ?? Str(edition, "subtitle");
        var series = CatalogText.ParseSeriesSubtitle(subtitle);
        if (series is not null)
        {
            subtitle = null;
        }

        var dataCover = Obj(data, "cover");
        var cover = CoverUrls.Normalize(Str(dataCover, "medium") ?? Str(dataCover, "large") ?? Str(dataCover, "small"));
        if (cover is null && FirstCoverId(Arr(edition, "covers")) is { } coverId)
        {
            cover = CoverUrls.ForOpenLibraryId(coverId);
        }

        var dims = DimensionParser.Parse(Str(edition, "physical_dimensions"));

        return new BookLookupResult
        {
            Isbn13 = isbn13,
            Title = title.Trim(),
            Subtitle = CatalogText.CleanSubtitle(subtitle),
            Authors = CatalogText.FilterByStatement(Names(Arr(data, "authors")), Str(edition, "by_statement")),
            Publisher = Names(Arr(data, "publishers")).FirstOrDefault() ?? Names(Arr(edition, "publishers")).FirstOrDefault(),
            PublishedYear = CatalogText.ExtractYear(Str(data, "publish_date") ?? Str(edition, "publish_date")),
            PageCount = Pages(Int(data, "number_of_pages") ?? Int(edition, "number_of_pages")),
            Genres = GenreMapper.FromSubjects(Names(Arr(data, "subjects"))),
            Series = series?.Series,
            SeriesNumber = series?.Number,
            CoverUrl = cover,
            HeightMm = dims?.HeightMm,
            WidthMm = dims?.WidthMm,
            ThicknessMm = dims?.ThicknessMm,
            Format = CatalogText.ParseFormat(Str(edition, "physical_format")),
            Source = BookSource.OpenLibrary,
            SourceKey = Str(data, "key") ?? Str(edition, "key"),
        };
    }

    internal static IReadOnlyList<BookSearchHit> ParseSearch(JsonElement root)
    {
        var hits = new List<BookSearchHit>();
        if (Arr(root, "docs") is not { } docs)
        {
            return hits;
        }

        foreach (var doc in docs.EnumerateArray())
        {
            var key = Str(doc, "key");
            var title = Str(doc, "title");
            if (key is null || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            // The best-matching edition (in the requested language) when Open Library returns one.
            JsonElement? edition = null;
            if (Arr(Obj(doc, "editions"), "docs") is { } eds)
            {
                foreach (var e in eds.EnumerateArray())
                {
                    edition = e;
                    break;
                }
            }

            string? isbn13 = null;
            if (Arr(edition, "isbn") is { } isbns)
            {
                // Prefer a 13-digit entry; fall back to converting an ISBN-10.
                foreach (var candidate in Strings(isbns).OrderByDescending(s => s.Length))
                {
                    if (Isbn.TryNormalize(candidate, out var n))
                    {
                        isbn13 = n;
                        break;
                    }
                }
            }

            var coverId = Long(edition, "cover_i") ?? Long(doc, "cover_i");

            hits.Add(new BookSearchHit
            {
                WorkKey = key,
                Title = title.Trim(),
                Subtitle = CatalogText.CleanSubtitle(Str(doc, "subtitle")),
                Authors = Strings(Arr(doc, "author_name")),
                FirstPublishYear = Int(doc, "first_publish_year"),
                CoverUrl = coverId is > 0 ? CoverUrls.ForOpenLibraryId(coverId.Value) : null,
                EditionCount = Int(doc, "edition_count") ?? 0,
                Isbn13 = isbn13,
                Publisher = Strings(Arr(edition, "publisher")).FirstOrDefault(),
                PageCount = Pages(Int(edition, "number_of_pages") ?? Int(doc, "number_of_pages_median")),
                Genres = GenreMapper.FromSubjects(Strings(Arr(doc, "subject"))),
            });
        }

        return hits;
    }

    private static BookSearchHit ToHit(BookLookupResult r)
        => new()
        {
            WorkKey = r.SourceKey ?? r.Isbn13 ?? r.Title,
            Title = r.Title,
            Subtitle = r.Subtitle,
            Authors = r.Authors,
            FirstPublishYear = r.PublishedYear,
            CoverUrl = r.CoverUrl,
            EditionCount = 1,
            Isbn13 = r.Isbn13,
            Publisher = r.Publisher,
            PageCount = r.PageCount,
            Genres = r.Genres,
            Source = r.Source,
        };

    private async Task<JsonDocument?> GetEditionAsync(string isbn13, CancellationToken ct)
    {
        try
        {
            return await GetJsonAsync($"isbn/{isbn13}.json", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null; // the edition record only adds extras
        }
    }

    /// <summary>GET and parse; null on 404. Other failures throw.</summary>
    private async Task<JsonDocument?> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        var uri = http.BaseAddress is null ? new Uri(DefaultBaseAddress, relativeUrl) : new Uri(relativeUrl, UriKind.Relative);
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    // ---- Tolerant JSON helpers ----

    private static JsonElement? Prop(JsonElement? e, string name)
        => e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static JsonElement? Obj(JsonElement? e, string name) => Prop(e, name) is { ValueKind: JsonValueKind.Object } v ? v : null;

    private static JsonElement? Arr(JsonElement? e, string name) => Prop(e, name) is { ValueKind: JsonValueKind.Array } v ? v : null;

    private static string? Str(JsonElement? e, string name) => Prop(e, name) switch
    {
        { ValueKind: JsonValueKind.String } v => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString(),
        { ValueKind: JsonValueKind.Number } v => v.GetRawText(),
        _ => null,
    };

    private static long? Long(JsonElement? e, string name) => Prop(e, name) switch
    {
        { ValueKind: JsonValueKind.Number } v when v.TryGetInt64(out var n) => n,
        { ValueKind: JsonValueKind.String } v when long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    private static int? Int(JsonElement? e, string name) => Long(e, name) is { } n and >= int.MinValue and <= int.MaxValue ? (int)n : null;

    /// <summary>Page counts below 10 are placeholders ("1") in Open Library data.</summary>
    private static int? Pages(int? pages) => pages is >= 10 and <= 20000 ? pages : null;

    private static long? FirstCoverId(JsonElement? covers)
    {
        if (covers is not { } arr)
        {
            return null;
        }

        foreach (var c in arr.EnumerateArray())
        {
            if (c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var id) && id > 0)
            {
                return id; // -1 marks a deleted cover
            }
        }

        return null;
    }

    private static List<string> Strings(JsonElement? arr)
    {
        var list = new List<string>();
        if (arr is { } a)
        {
            foreach (var v in a.EnumerateArray())
            {
                if (v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                {
                    list.Add(v.GetString()!.Trim());
                }
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Arrays of strings or of objects with a "name": both shapes occur.</summary>
    private static List<string> Names(JsonElement? arr)
    {
        var list = new List<string>();
        if (arr is { } a)
        {
            foreach (var v in a.EnumerateArray())
            {
                var name = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Object => Str(v, "name"),
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(name.Trim());
                }
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
