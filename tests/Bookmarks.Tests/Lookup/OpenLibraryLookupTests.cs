using System.Net;
using System.Text;
using Bookmarks.Core.Lookup;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests.Lookup;

public class OpenLibraryLookupTests
{
    [Fact]
    public async Task Isbn_lookup_combines_data_and_edition()
    {
        var (lookup, handler) = Create();

        var r = await lookup.FindByIsbnAsync("978-0-593-13520-4");

        Assert.NotNull(r);
        Assert.Equal("9780593135204", r.Isbn13);
        Assert.Equal("Project Hail Mary", r.Title);
        Assert.Null(r.Subtitle);
        Assert.Equal(new[] { "Andy Weir" }, r.Authors.ToArray());
        Assert.Equal("Ballantine Books", r.Publisher);
        Assert.Equal(2021, r.PublishedYear);
        Assert.Equal(496, r.PageCount);
        Assert.Equal(new[] { "Science Fiction" }, r.Genres.ToArray());
        Assert.Equal("https://covers.openlibrary.org/b/id/12455001-M.jpg", r.CoverUrl);
        Assert.Equal(242, r.HeightMm);
        Assert.Equal(162, r.WidthMm);
        Assert.Equal(39, r.ThicknessMm);
        Assert.Equal(BookFormat.Hardcover, r.Format);
        Assert.Equal(BookSource.OpenLibrary, r.Source);
        Assert.Equal("/books/OL30036715M", r.SourceKey);

        Assert.Contains("api/books?bibkeys=ISBN:9780593135204&jscmd=data&format=json", handler.Requests);
        Assert.Contains("isbn/9780593135204.json", handler.Requests);
    }

    [Fact]
    public async Task Series_comes_from_the_subtitle_and_inches_are_converted()
    {
        var (lookup, _) = Create();

        var r = await lookup.FindByIsbnAsync("0765326353");

        Assert.NotNull(r);
        Assert.Equal("The Way of Kings", r.Title);
        Assert.Null(r.Subtitle);
        Assert.Equal("The Stormlight Archive", r.Series);
        Assert.Equal(1m, r.SeriesNumber);
        Assert.Equal(2010, r.PublishedYear);
        Assert.Equal(241, r.HeightMm);
        Assert.Equal(new[] { "Fantasy" }, r.Genres.ToArray());
    }

    [Fact]
    public async Task Authors_resolved_from_the_work_are_used_when_the_edition_has_none()
    {
        var (lookup, _) = Create();

        var r = await lookup.FindByIsbnAsync("9780547928227");

        Assert.NotNull(r);
        Assert.Equal(new[] { "J.R.R. Tolkien" }, r.Authors.ToArray());
        Assert.Equal(BookFormat.Paperback, r.Format);
        Assert.Null(r.HeightMm);
    }

    [Fact]
    public async Task Missing_edition_record_still_returns_the_data()
    {
        var (lookup, handler) = Create();
        handler.Remove("isbn/9780547928227.json");

        var r = await lookup.FindByIsbnAsync("9780547928227");

        Assert.NotNull(r);
        Assert.Equal("The Hobbit", r.Title);
        Assert.Null(r.Format);
        Assert.Equal("https://covers.openlibrary.org/b/id/12003329-M.jpg", r.CoverUrl);
    }

    [Fact]
    public async Task Edition_alone_is_enough()
    {
        var (lookup, handler) = Create();
        handler.Set("api/books?bibkeys=ISBN:9780547928227&jscmd=data&format=json", "{}");

        var r = await lookup.FindByIsbnAsync("9780547928227");

        Assert.NotNull(r);
        Assert.Equal("The Hobbit", r.Title);
        Assert.Empty(r.Authors);
        Assert.Equal("https://covers.openlibrary.org/b/id/12003329-M.jpg", r.CoverUrl);
    }

    [Fact]
    public async Task An_edition_without_a_cover_borrows_the_works_cover_and_subjects()
    {
        // Real case: the 2018 hardcover of Educated has no cover or subjects of its own (2026-10-10).
        var (lookup, handler) = Create();
        handler.Set("api/books?bibkeys=ISBN:9780525589983&jscmd=data&format=json",
            "{\"ISBN:9780525589983\":{\"key\":\"/books/OL26620627M\",\"title\":\"Educated\",\"subtitle\":\"a memoir\",\"authors\":[{\"name\":\"Tara Westover\"}],\"publish_date\":\"2018\"}}");
        handler.Set("isbn/9780525589983.json", "{\"title\":\"Educated\",\"works\":[{\"key\":\"/works/OL18139176W\"}]}");
        handler.Set("works/OL18139176W.json", "{\"title\":\"Educated\",\"covers\":[-1,8314077,8815122],\"subjects\":[\"Autobiography\",\"Survivalism\"]}");

        var r = await lookup.FindByIsbnAsync("9780525589983");

        Assert.NotNull(r);
        Assert.Equal("A memoir", r.Subtitle);
        Assert.Equal("https://covers.openlibrary.org/b/id/8314077-M.jpg", r.CoverUrl);
        Assert.Equal(new[] { "Biography" }, r.Genres.ToArray());
        Assert.Contains("works/OL18139176W.json", handler.Requests);
    }

    [Fact]
    public async Task An_edition_with_a_cover_and_genres_makes_no_work_request()
    {
        var (lookup, handler) = Create();

        await lookup.FindByIsbnAsync("9780593135204");

        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("works/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_missing_work_record_leaves_the_result_as_it_was()
    {
        var (lookup, handler) = Create();
        handler.Set("api/books?bibkeys=ISBN:9780525589983&jscmd=data&format=json", "{\"ISBN:9780525589983\":{\"title\":\"Educated\"}}");
        handler.Set("isbn/9780525589983.json", "{\"title\":\"Educated\",\"works\":[{\"key\":\"/works/OL18139176W\"}]}");

        var r = await lookup.FindByIsbnAsync("9780525589983");

        Assert.NotNull(r);
        Assert.Null(r.CoverUrl);
    }

    [Theory]
    [InlineData("/works/OL18139176W", "/works/OL18139176W")]
    [InlineData("/works/../../evil", null)]
    [InlineData("/authors/OL26320A", null)]
    [InlineData("/works/OLW", null)]
    public void Work_keys_are_validated(string key, string? expected)
    {
        using var doc = System.Text.Json.JsonDocument.Parse("{\"works\":[{\"key\":\"" + key + "\"}]}");
        Assert.Equal(expected, OpenLibraryLookup.WorkKey(doc.RootElement));
    }

    [Fact]
    public async Task Cover_choices_come_from_the_edition_the_work_and_english_editions_first()
    {
        var (lookup, handler) = Create();
        handler.Set("isbn/9780525589983.json", "{\"covers\":[1,-1],\"works\":[{\"key\":\"/works/OL18139176W\"}]}");
        handler.Set("works/OL18139176W.json", "{\"covers\":[2,3,1]}");
        handler.Set("works/OL18139176W/editions.json?limit=50",
            "{\"entries\":[{\"covers\":[4],\"languages\":[{\"key\":\"/languages/fre\"}]},{\"covers\":[5,2],\"languages\":[{\"key\":\"/languages/eng\"}]},{}]}");

        var covers = await lookup.FindCoversAsync("9780525589983", "Educated", "Tara Westover");

        Assert.Equal(
            new[] { 1L, 2, 3, 5, 4 }.Select(id => $"https://covers.openlibrary.org/b/id/{id}-M.jpg").ToArray(),
            covers.ToArray());
    }

    [Fact]
    public async Task Cover_choices_also_include_other_records_of_the_same_title()
    {
        var (lookup, handler) = Create();
        handler.Set("isbn/9780525589983.json", "{\"covers\":[1],\"works\":[{\"key\":\"/works/OL18139176W\"}]}");
        handler.Set("works/OL18139176W.json", "{\"covers\":[2]}");
        handler.SetPrefix("search.json?",
            "{\"docs\":[{\"key\":\"/works/OL18139176W\",\"title\":\"Educated\"},{\"key\":\"/works/OL2W\",\"title\":\"Educated: A Memoir\"},{\"key\":\"/works/OL3W\",\"title\":\"Uneducated\"}]}");
        handler.Set("works/OL2W.json", "{\"covers\":[3,2]}");
        handler.Set("works/OL3W.json", "{\"covers\":[99]}");

        var covers = await lookup.FindCoversAsync("9780525589983", "Educated", "Tara Westover");

        Assert.Equal(
            new[] { 1L, 2, 3 }.Select(id => $"https://covers.openlibrary.org/b/id/{id}-M.jpg").ToArray(),
            covers.ToArray());
        Assert.DoesNotContain("works/OL3W.json", handler.Requests); // a different book
    }

    [Fact]
    public async Task Without_an_isbn_cover_choices_are_found_by_title()
    {
        var (lookup, handler) = Create();
        handler.Set("works/OL15358691W.json", "{\"covers\":[7429768]}");

        var covers = await lookup.FindCoversAsync(null, "The Way of Kings", "Brandon Sanderson");

        Assert.Equal(new[] { "https://covers.openlibrary.org/b/id/7429768-M.jpg" }, covers.ToArray());
        Assert.Contains(handler.Requests, r => r.StartsWith("search.json?q=The%20Way%20of%20Kings%20Brandon%20Sanderson&fields=key,title&limit=10", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_book_means_no_cover_choices()
    {
        var (lookup, _) = Create();
        Assert.Empty(await lookup.FindCoversAsync(null, null, null));
    }

    [Fact]
    public void Cover_choices_are_capped()
    {
        using var work = System.Text.Json.JsonDocument.Parse("{\"covers\":[" + string.Join(',', Enumerable.Range(1, 100)) + "]}");
        var works = new[] { ((System.Text.Json.JsonElement?)work.RootElement, (System.Text.Json.JsonElement?)null) };
        Assert.Equal(OpenLibraryLookup.MaxCoverChoices, OpenLibraryLookup.CollectCoverIds(null, works).Count);
    }

    [Fact]
    public async Task Unknown_isbn_returns_null()
    {
        var (lookup, _) = Create();
        Assert.Null(await lookup.FindByIsbnAsync("9781250000002"));
    }

    [Fact]
    public async Task Invalid_isbn_throws_before_any_request()
    {
        var (lookup, handler) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => lookup.FindByIsbnAsync("9780547928228"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Server_errors_are_thrown_not_reported_as_not_found()
    {
        var (lookup, handler) = Create();
        handler.Fail = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<HttpRequestException>(() => lookup.FindByIsbnAsync("9780547928227"));
    }

    [Fact]
    public async Task Search_returns_works_with_their_best_edition()
    {
        var (lookup, handler) = Create();

        var hits = await lookup.SearchAsync("way of kings sanderson", limit: 3);

        Assert.Equal(3, hits.Count);
        var first = hits[0];
        Assert.Equal("/works/OL15358691W", first.WorkKey);
        Assert.Equal("The Way of Kings", first.Title);
        Assert.Equal(new[] { "Brandon Sanderson" }, first.Authors.ToArray());
        Assert.Equal(2010, first.FirstPublishYear);
        Assert.Equal(30, first.EditionCount);
        Assert.Equal("9780765326355", first.Isbn13);
        Assert.Equal("Tor", first.Publisher);
        Assert.Equal("https://covers.openlibrary.org/b/id/8890435-M.jpg", first.CoverUrl); // the edition's cover
        Assert.Equal(1008, first.PageCount);
        Assert.Equal(new[] { "Fantasy" }, first.Genres.ToArray());

        Assert.Null(hits[2].PageCount); // "1" is a placeholder
        Assert.Single(handler.Requests);
        Assert.StartsWith("search.json?q=way%20of%20kings%20sanderson&fields=", handler.Requests[0], StringComparison.Ordinal);
        Assert.Contains("&limit=3&lang=en", handler.Requests[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Searching_for_an_isbn_does_an_isbn_lookup()
    {
        var (lookup, handler) = Create();

        var hits = await lookup.SearchAsync("0-7653-2635-3");

        var hit = Assert.Single(hits);
        Assert.Equal("9780765326355", hit.Isbn13);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("search.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Hit_without_isbn_converts_to_a_result()
    {
        var hit = new BookSearchHit { WorkKey = "/works/OL1W", Title = "Piranesi", Authors = ["Susanna Clarke"], FirstPublishYear = 2020 };
        var r = hit.ToResult();
        Assert.Equal("Piranesi", r.Title);
        Assert.Equal(2020, r.PublishedYear);
        Assert.Null(r.Isbn13);
        Assert.Equal(BookSource.OpenLibrary, r.Source);
    }

    private static (OpenLibraryLookup, FixtureHandler) Create()
    {
        var handler = new FixtureHandler();
        handler.Set("api/books?bibkeys=ISBN:9780593135204&jscmd=data&format=json", Fixture("ol-data-9780593135204.json"));
        handler.Set("isbn/9780593135204.json", Fixture("ol-edition-9780593135204.json"));
        handler.Set("api/books?bibkeys=ISBN:9780765326355&jscmd=data&format=json", Fixture("ol-data-9780765326355.json"));
        handler.Set("isbn/9780765326355.json", Fixture("ol-edition-9780765326355.json"));
        handler.Set("api/books?bibkeys=ISBN:9780547928227&jscmd=data&format=json", Fixture("ol-data-9780547928227.json"));
        handler.Set("isbn/9780547928227.json", Fixture("ol-edition-9780547928227.json"));
        handler.Set("api/books?bibkeys=ISBN:9781250000002&jscmd=data&format=json", "{}");
        handler.SetPrefix("search.json?", Fixture("ol-search-way-of-kings.json"));

        var http = new HttpClient(handler) { BaseAddress = OpenLibraryLookup.DefaultBaseAddress };
        return (new OpenLibraryLookup(http), handler);
    }

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Lookup", "Fixtures", name));

    /// <summary>Serves canned JSON by path-and-query (relative to the base address); 404 for anything else.</summary>
    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _prefix = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public HttpStatusCode? Fail { get; set; }

        public void Set(string pathAndQuery, string json) => _exact[pathAndQuery] = json;

        public void SetPrefix(string prefix, string json) => _prefix[prefix] = json;

        public void Remove(string pathAndQuery) => _exact.Remove(pathAndQuery);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri!.PathAndQuery.TrimStart('/');
            lock (Requests)
            {
                Requests.Add(key);
            }

            if (Fail is { } status)
            {
                return Task.FromResult(new HttpResponseMessage(status));
            }

            var json = _exact.TryGetValue(key, out var exact) ? exact
                : _prefix.FirstOrDefault(p => key.StartsWith(p.Key, StringComparison.Ordinal)).Value;

            return Task.FromResult(json is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
