using System.Globalization;
using System.Text.RegularExpressions;

namespace Bookmarks.Core.Lookup;

/// <summary>Physical size of a book in millimetres; any part may be unknown.</summary>
public readonly record struct BookDimensions(int? HeightMm, int? WidthMm, int? ThicknessMm);

/// <summary>
/// Parses free-text sizes from catalog records: "24.2 x 16.2 x 3.9 centimeters", "9.5 x 6.4 x 2 inches",
/// "20 x 13 cm", or the library-catalog classic "24 cm." (height only).
/// </summary>
/// <remarks>
/// The order of the numbers varies between records, so the largest is taken as height, the next as width and
/// the smallest as thickness. Values outside sensible ranges are dropped rather than trusted.
/// </remarks>
public static partial class DimensionParser
{
    public static BookDimensions? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // "9.5 x 6.4 x 2 inches; 2.1 pounds": the weight is not a dimension.
        var s = text.Split(';')[0].ToLowerInvariant();

        double factor;
        if (Inches().IsMatch(s))
        {
            factor = 25.4;
        }
        else if (Millimetres().IsMatch(s))
        {
            factor = 1;
        }
        else if (Centimetres().IsMatch(s))
        {
            factor = 10;
        }
        else
        {
            return null; // no unit: too ambiguous to guess
        }

        var values = Number().Matches(s)
            .Select(m => double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .Where(v => v > 0)
            .Take(3)
            .Select(v => (int)Math.Round(v * factor))
            .OrderByDescending(v => v)
            .ToList();

        if (values.Count == 0)
        {
            return null;
        }

        int? height = values[0] is >= 80 and <= 500 ? values[0] : null;
        int? width = values.Count > 1 && values[1] is >= 50 and <= 400 ? values[1] : null;
        int? thickness = values.Count > 2 && values[2] is >= 1 and <= 150 ? values[2] : null;

        return height is null && width is null && thickness is null
            ? null
            : new BookDimensions(height, width, thickness);
    }

    [GeneratedRegex(@"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Number();

    [GeneratedRegex(@"inch|\d\s*(""|”|in\b)", RegexOptions.CultureInvariant)]
    private static partial Regex Inches();

    [GeneratedRegex(@"\d\s*mm\b|millimet", RegexOptions.CultureInvariant)]
    private static partial Regex Millimetres();

    [GeneratedRegex(@"\d\s*cm\b|centimet", RegexOptions.CultureInvariant)]
    private static partial Regex Centimetres();
}
