using System.Globalization;
using System.Text.RegularExpressions;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Lookup;

/// <summary>Small parsers for the free-text fields catalog records are full of.</summary>
public static partial class CatalogText
{
    private static readonly string[] NumberWords =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    private static readonly string[] OrdinalWords =
        ["", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth", "eleventh", "twelfth"];

    /// <summary>"August 31st 2010", "May 04, 2021", "c1995", "2012" → the year.</summary>
    public static int? ExtractYear(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var m = Year().Match(text);
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Catalog "physical_format" ("hardcover", "Trade paperback", "Mass Market Paperback") → our format.</summary>
    public static BookFormat? ParseFormat(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.ToLowerInvariant();
        if (s.Contains("leather", StringComparison.Ordinal))
        {
            return BookFormat.LeatherBound;
        }

        if (s.Contains("mass market", StringComparison.Ordinal) || s.Contains("mass-market", StringComparison.Ordinal) || s.Contains("pocket", StringComparison.Ordinal))
        {
            return BookFormat.MassMarketPaperback;
        }

        if (s.Contains("hardcover", StringComparison.Ordinal) || s.Contains("hardback", StringComparison.Ordinal)
            || s.Contains("hard cover", StringComparison.Ordinal) || s.Contains("library binding", StringComparison.Ordinal)
            || s.Contains("cloth", StringComparison.Ordinal))
        {
            return BookFormat.Hardcover;
        }

        if (s.Contains("paperback", StringComparison.Ordinal) || s.Contains("softcover", StringComparison.Ordinal)
            || s.Contains("soft cover", StringComparison.Ordinal) || s.Contains("trade paper", StringComparison.Ordinal))
        {
            return BookFormat.Paperback;
        }

        return null;
    }

    /// <summary>
    /// Many editions carry the series only in the subtitle: "Book One of the Stormlight Archive",
    /// "Volume 2 of The Expanse". Returns the series and number, or null when the subtitle isn't like that.
    /// </summary>
    public static (string Series, decimal? Number)? ParseSeriesSubtitle(string? subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle))
        {
            return null;
        }

        var m = SeriesSubtitle().Match(subtitle.Trim());
        if (!m.Success)
        {
            // The ordinal form needs a real number, so "A Novel of the Sprawl" isn't taken as a series.
            m = OrdinalSeriesSubtitle().Match(subtitle.Trim());
            if (!m.Success || ParseNumber(m.Groups["n"].Value) is null)
            {
                return null;
            }
        }

        var series = m.Groups["series"].Value.Trim().TrimEnd('.');
        if (series.Length == 0)
        {
            return null;
        }

        series = char.ToUpperInvariant(series[0]) + series[1..];
        return (series, ParseNumber(m.Groups["n"].Value));
    }

    /// <summary>
    /// Open Library sometimes lists translators or narrators as authors of a work ("Tara Westover, Orah Danḳner").
    /// When the edition's "by" statement ("by Tara Westover") names some of them, only those are kept; otherwise the
    /// list is returned unchanged.
    /// </summary>
    public static IReadOnlyList<string> FilterByStatement(IReadOnlyList<string> authors, string? byStatement)
    {
        ArgumentNullException.ThrowIfNull(authors);
        if (authors.Count < 2 || string.IsNullOrWhiteSpace(byStatement))
        {
            return authors;
        }

        var by = Fold(byStatement);
        var credited = authors.Where(a => Surname(a) is { Length: > 1 } s && by.Contains(s, StringComparison.Ordinal)).ToList();
        return credited.Count > 0 ? credited : authors;

        static string? Surname(string name)
        {
            var parts = Fold(name).Split(new[] { ' ', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? null : parts[^1];
        }
    }

    /// <summary>Lower case without accents, so "Danḳner" matches "Dankner".</summary>
    private static string Fold(string text)
    {
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Subtitles that add nothing ("A Novel", "A Novel.") are dropped; catalog records often store the rest in lower
    /// case ("a memoir"), so the first letter is capitalized.
    /// </summary>
    public static string? CleanSubtitle(string? subtitle)
    {
        var s = subtitle?.Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(s) || s.Equals("a novel", StringComparison.OrdinalIgnoreCase) || s.Equals("novel", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = subtitle!.Trim();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private static decimal? ParseNumber(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var suffix in new[] { "st", "nd", "rd", "th" })
        {
            if (lower.Length > 2 && lower.EndsWith(suffix, StringComparison.Ordinal) && char.IsAsciiDigit(lower[^3]))
            {
                lower = lower[..^2];
                break;
            }
        }

        if (decimal.TryParse(lower, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        var i = Array.IndexOf(NumberWords, lower);
        if (i > 0)
        {
            return i;
        }

        i = Array.IndexOf(OrdinalWords, lower);
        return i > 0 ? i : null;
    }

    [GeneratedRegex(@"(?<!\d)(1[5-9]\d\d|20\d\d)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Year();

    [GeneratedRegex(@"^(?:the\s+)?(?:book|volume|vol\.?|part|no\.?)\s+(?<n>\d+(?:\.\d+)?|[a-z]+)\s+(?:of|in)\s+(?<series>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeriesSubtitle();

    // "The Second Book of the Kingkiller Chronicle", "3rd volume in …"
    [GeneratedRegex(@"^(?:the\s+)?(?<n>[a-z]+|\d+(?:st|nd|rd|th))\s+(?:book|volume|part|novel)\s+(?:of|in)\s+(?<series>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrdinalSeriesSubtitle();
}
