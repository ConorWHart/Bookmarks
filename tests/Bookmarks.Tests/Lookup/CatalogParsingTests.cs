using Bookmarks.Core.Lookup;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests.Lookup;

public class CatalogParsingTests
{
    [Theory]
    [InlineData("24.2 x 16.2 x 3.9 centimeters", 242, 162, 39)]
    [InlineData("9.5 x 6.4 x 2 inches", 241, 163, 51)]
    [InlineData("8.25 x 5.5 x 1.1 inches; 1.2 pounds", 210, 140, 28)]
    [InlineData("2 x 6.4 x 9.5 inches", 241, 163, 51)] // order varies between records
    [InlineData("20 x 13 cm", 200, 130, null)]
    [InlineData("240 x 160 x 30 mm", 240, 160, 30)]
    [InlineData("24cm", 240, null, null)]
    [InlineData("24 cm.", 240, null, null)]
    [InlineData("9.5\" x 6.4\"", 241, 163, null)]
    public void Dimensions_parse(string text, int? height, int? width, int? thickness)
        => Assert.Equal(new BookDimensions(height, width, thickness), DimensionParser.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24 x 16 x 4")] // no unit
    [InlineData("large print")]
    [InlineData("2 cm")] // nothing in a sensible range
    public void Dimensions_unknown(string? text)
        => Assert.Null(DimensionParser.Parse(text));

    [Fact]
    public void Dimensions_out_of_range_parts_are_dropped()
        => Assert.Equal(new BookDimensions(242, 162, null), DimensionParser.Parse("24.2 x 16.2 x 0.01 centimeters"));

    [Theory]
    [InlineData("August 31st 2010", 2010)]
    [InlineData("May 04, 2021", 2021)]
    [InlineData("c1995", 1995)]
    [InlineData("2012", 2012)]
    [InlineData("[1937]", 1937)]
    [InlineData("n.d.", null)]
    [InlineData("12345", null)]
    public void Year_is_extracted(string text, int? year)
        => Assert.Equal(year, CatalogText.ExtractYear(text));

    [Theory]
    [InlineData("hardcover", BookFormat.Hardcover)]
    [InlineData("Hardback", BookFormat.Hardcover)]
    [InlineData("Library Binding", BookFormat.Hardcover)]
    [InlineData("Trade paperback", BookFormat.Paperback)]
    [InlineData("Paperback", BookFormat.Paperback)]
    [InlineData("Mass Market Paperback", BookFormat.MassMarketPaperback)]
    [InlineData("Leather Bound", BookFormat.LeatherBound)]
    [InlineData("Audio CD", null)]
    [InlineData(null, null)]
    public void Format_is_mapped(string? text, BookFormat? format)
        => Assert.Equal(format, CatalogText.ParseFormat(text));

    [Theory]
    [InlineData("Book One of the Stormlight Archive", "The Stormlight Archive", 1)]
    [InlineData("Volume 2 of The Expanse", "The Expanse", 2)]
    [InlineData("Book 3 in the Wheel of Time", "The Wheel of Time", 3)]
    [InlineData("The Second Book of the Kingkiller Chronicle", "The Kingkiller Chronicle", 2)]
    public void Series_subtitle_is_parsed(string subtitle, string series, int number)
    {
        var parsed = CatalogText.ParseSeriesSubtitle(subtitle);
        Assert.NotNull(parsed);
        Assert.Equal(series, parsed.Value.Series);
        Assert.Equal(number, parsed.Value.Number);
    }

    [Theory]
    [InlineData("or There and Back Again")]
    [InlineData("A Novel")]
    [InlineData(null)]
    public void Ordinary_subtitles_are_not_series(string? subtitle)
        => Assert.Null(CatalogText.ParseSeriesSubtitle(subtitle));

    [Theory]
    [InlineData("A Novel", null)]
    [InlineData("a novel.", null)]
    [InlineData(" or There and Back Again ", "Or There and Back Again")]
    [InlineData("a memoir", "A memoir")]
    public void Subtitle_is_cleaned(string subtitle, string? expected)
        => Assert.Equal(expected, CatalogText.CleanSubtitle(subtitle));

    [Fact]
    public void Authors_missing_from_the_by_statement_are_dropped()
        => Assert.Equal(new[] { "Tara Westover" },
            CatalogText.FilterByStatement(new[] { "Tara Westover", "Orah Danḳner" }, "Tara Westover").ToArray());

    [Fact]
    public void Co_authors_named_in_the_by_statement_are_kept()
        => Assert.Equal(new[] { "Terry Pratchett", "Neil Gaiman" },
            CatalogText.FilterByStatement(new[] { "Terry Pratchett", "Neil Gaiman" }, "by Terry Pratchett & Neil Gaiman.").ToArray());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("edited by somebody else")]
    public void Without_a_useful_by_statement_authors_are_unchanged(string? byStatement)
        => Assert.Equal(new[] { "Tara Westover", "Orah Danḳner" },
            CatalogText.FilterByStatement(new[] { "Tara Westover", "Orah Danḳner" }, byStatement).ToArray());

    [Theory]
    [InlineData("The Way of Kings", "way of kings")]
    [InlineData("Way of Kings!", "way of kings")]
    [InlineData("Educated: A Memoir", "educated a memoir")]
    [InlineData("Les Misérables", "les miserables")]
    [InlineData("The", "the")]
    [InlineData(null, "")]
    public void Titles_are_normalized_for_matching(string? title, string expected)
        => Assert.Equal(expected, CatalogText.NormalizeTitle(title));

    [Fact]
    public void Genres_come_from_a_small_vocabulary()
    {
        var subjects = new[]
        {
            "hard science-fiction", "nyt:hardcover-fiction=2021-05-23", "New York Times bestseller",
            "Fiction, science fiction, action & adventure", "End of the world", "Fiction", "Astronauts",
        };
        Assert.Equal(new[] { "Science Fiction" }, GenreMapper.FromSubjects(subjects).ToArray());
    }

    [Fact]
    public void Genres_are_capped_and_ordered_by_subject()
    {
        var subjects = new[] { "Detective and mystery stories", "Historical fiction", "Romance", "Fantasy fiction", "History" };
        Assert.Equal(new[] { "Mystery", "Historical Fiction", "Romance" }, GenreMapper.FromSubjects(subjects).ToArray());
    }

    [Fact]
    public void Plain_fiction_is_the_fallback()
        => Assert.Equal(new[] { "Fiction" }, GenreMapper.FromSubjects(new[] { "Kings and rulers", "Fiction" }).ToArray());

    [Fact]
    public void Words_inside_other_words_do_not_match()
        => Assert.Empty(GenreMapper.FromSubjects(new[] { "Party planning", "Heartbreak", "Steenbergen" }));

    [Theory]
    [InlineData("https://covers.openlibrary.org/b/id/12455001-M.jpg", CoverSize.Small, "https://covers.openlibrary.org/b/id/12455001-S.jpg")]
    [InlineData("http://covers.openlibrary.org/b/isbn/9780547928227-L.jpg", CoverSize.Medium, "https://covers.openlibrary.org/b/isbn/9780547928227-M.jpg")]
    [InlineData("https://example.com/cover.jpg", CoverSize.Small, "https://example.com/cover.jpg")]
    public void Cover_urls_resize(string url, CoverSize size, string expected)
        => Assert.Equal(expected, CoverUrls.WithSize(url, size));
}
