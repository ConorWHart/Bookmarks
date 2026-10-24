using System.Globalization;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

/// <summary>A generated spine: one of the accent styles with its colours.</summary>
public sealed record SpineDesign(SpineStyle Style, string SpineColor, string AccentColor, string TextColor);

/// <summary>
/// Makes spine designs from a cover's colours (Phase 7e, "Design from cover"). Each design is one of the accent styles
/// (<see cref="Styles"/>) with a base colour, an accent that stands apart from it, and lettering that reads on it.
/// Designs are numbered so "Shuffle" can step through them; a book's first design comes from its id, so books by the
/// same cover colours don't all look the same and the same book always starts on the same one.
/// </summary>
public static class SpineDesigner
{
    /// <summary>The styles that use an accent colour, in the order designs cycle through them.</summary>
    public static readonly IReadOnlyList<SpineStyle> Styles =
        [SpineStyle.Penguin, SpineStyle.Deco, SpineStyle.Stripe, SpineStyle.TwoTone];

    /// <summary>Cream lettering: softer than white on dark spines.</summary>
    public const string Cream = "#F4EBD9";

    /// <summary>Colours closer than this (RGB distance) look like the same colour on a narrow spine.</summary>
    private const double MinAccentDistance = 70;

    /// <summary>Used when there's no colour at all to start from.</summary>
    private const string Fallback = "#2F4858";

    /// <summary>True for the styles drawn with an accent colour.</summary>
    public static bool UsesAccent(SpineStyle? style) => style is { } s && Styles.Contains(s);

    /// <summary>How many different designs <paramref name="palette"/> gives (styles × colour pairings).</summary>
    public static int VariantCount(IReadOnlyList<string> palette) => Styles.Count * Pairs(palette).Count;

    /// <summary>The first design for a book: picked by its id, so it's stable but varies from book to book.</summary>
    public static int StartVariant(Guid seed, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var bytes = seed.ToByteArray();
        var hash = BitConverter.ToUInt32(bytes, 0) ^ BitConverter.ToUInt32(bytes, 12);
        return (int)(hash % (uint)count);
    }

    /// <summary>Design number <paramref name="variant"/> (wraps around) for the cover colours in <paramref name="palette"/>.</summary>
    /// <param name="palette">The cover's colours, most prominent first ("#RRGGBB"); may be empty.</param>
    public static SpineDesign Design(IReadOnlyList<string> palette, int variant)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var pairs = Pairs(palette);
        var count = Styles.Count * pairs.Count;
        var v = ((variant % count) + count) % count;

        // Styles change fastest, so Shuffle shows a new look each time before trying other colours.
        var style = Styles[v % Styles.Count];
        var (spine, accent) = pairs[v / Styles.Count];
        if (style == SpineStyle.TwoTone && BestOnBoth(spine, accent) < ColorUtil.ComfortableContrast)
        {
            // No lettering reads on both halves: blend into a deeper or lighter shade of the base instead, away from
            // the lettering, so the title stays readable all the way along.
            var text = Lettering(SpineStyle.Penguin, spine, accent);
            var lightText = ColorUtil.ContrastRatio(text, "#000000") > ColorUtil.ContrastRatio(text, "#FFFFFF");
            accent = ColorUtil.Shade(spine, lightText ? -0.45 : 0.45)!;
            if (Distance(accent, spine) < 30)
            {
                accent = ColorUtil.Shade(spine, lightText ? 0.3 : -0.3)!; // already near black or white
            }
        }

        return new SpineDesign(style, spine, accent, Lettering(style, spine, accent));
    }

    /// <summary>
    /// Base and accent pairings: each usable cover colour as the base, with every other cover colour that stands apart
    /// from it, then a complementary colour made from it (always available, so a one-colour cover still gets accents).
    /// </summary>
    private static List<(string Spine, string Accent)> Pairs(IReadOnlyList<string> palette)
    {
        var colors = palette
            .Select(Normalize)
            .Where(c => c is not null)
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();
        if (colors.Count == 0)
        {
            colors.Add(Fallback);
        }

        var pairs = new List<(string, string)>();
        foreach (var spine in colors.Take(3))
        {
            foreach (var accent in colors)
            {
                if (accent != spine && Distance(spine, accent) >= MinAccentDistance)
                {
                    pairs.Add((spine, accent));
                }
            }
        }

        foreach (var spine in colors.Take(2))
        {
            pairs.Add((spine, Complement(spine)));
        }

        return pairs;
    }

    /// <summary>
    /// Lettering that reads on the spine: gilt on dark Deco spines, cream or near-black elsewhere. Two-tone titles sit
    /// over both colours, so they take whichever reads best on the weaker of the two.
    /// </summary>
    private static string Lettering(SpineStyle style, string spine, string accent)
    {
        string[] candidates = style switch
        {
            SpineStyle.Deco => [ColorUtil.Gilt, Cream, ColorUtil.DarkText],
            _ => [Cream, ColorUtil.LightText, ColorUtil.DarkText],
        };

        Func<string, double> score = style == SpineStyle.TwoTone
            ? c => Math.Min(ColorUtil.ContrastRatio(c, spine), ColorUtil.ContrastRatio(c, accent))
            : c => ColorUtil.ContrastRatio(c, spine);

        // The first candidate that's comfortable to read, else the most readable one.
        return candidates.FirstOrDefault(c => score(c) >= 4.5)
            ?? candidates.OrderByDescending(score).First();
    }

    private static double BestOnBoth(string spine, string accent)
        => new[] { Cream, ColorUtil.LightText, ColorUtil.DarkText }
            .Max(c => Math.Min(ColorUtil.ContrastRatio(c, spine), ColorUtil.ContrastRatio(c, accent)));

    /// <summary>
    /// The colour opposite on the colour wheel, kept rich enough to read as a colour, and lighter or darker than the
    /// base so the two separate even for someone who can't tell the hues apart.
    /// </summary>
    public static string Complement(string hex)
    {
        if (!ColorUtil.TryParseHex(hex, out var rgb))
        {
            return Complement(Fallback);
        }

        var (h, s, l) = ColorUtil.ToHsl(rgb);
        var hue = (h + 180) % 360;
        var saturation = Math.Clamp(Math.Max(s, 0.35), 0, 0.85);
        var lightness = l >= 0.5 ? Math.Clamp(l - 0.35, 0.15, 0.4) : Math.Clamp(l + 0.35, 0.55, 0.8);
        return FromHsl(hue, saturation, lightness);
    }

    private static string? Normalize(string? hex)
        => ColorUtil.TryParseHex(hex, out var rgb) ? ToHex(rgb.R, rgb.G, rgb.B) : null;

    private static double Distance(string a, string b)
    {
        ColorUtil.TryParseHex(a, out var x);
        ColorUtil.TryParseHex(b, out var y);
        double dr = x.R - y.R, dg = x.G - y.G, db = x.B - y.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static string FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs((2 * l) - 1)) * s;
        var x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        var m = l - (c / 2);
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return ToHex(Byte(r + m), Byte(g + m), Byte(b + m));
    }

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

    private static string ToHex(byte r, byte g, byte b)
        => "#" + r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture)
            + b.ToString("X2", CultureInfo.InvariantCulture);
}
