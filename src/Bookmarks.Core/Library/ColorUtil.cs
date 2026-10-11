using System.Globalization;

namespace Bookmarks.Core.Library;

public static class ColorUtil
{
    public const string LightText = "#FFFFFF";
    public const string DarkText = "#1A1A1A";
    public const string Gilt = "#E9C46A";

    /// <summary>Contrast for comfortable reading of large text like spine titles (WCAG AA for large text).</summary>
    public const double ComfortableContrast = 3.0;

    /// <summary>Below this a chosen text color is effectively unreadable, so the automatic color is used instead.</summary>
    public const double MinimumCustomContrast = 2.0;

    /// <summary>Parses #RGB or #RRGGBB. Returns false for anything else.</summary>
    public static bool TryParseHex(string? hex, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3)
        {
            s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
        }

        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        rgb = ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>Hue [0,360), saturation and lightness [0,1].</summary>
    public static (double H, double S, double L) ToHsl((byte R, byte G, byte B) rgb)
    {
        var r = rgb.R / 255d;
        var g = rgb.G / 255d;
        var b = rgb.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var d = max - min;

        if (d < 1e-9)
        {
            return (0, 0, l);
        }

        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = ((g - b) / d) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = ((b - r) / d) + 2;
        }
        else
        {
            h = ((r - g) / d) + 4;
        }

        return (h * 60, s, l);
    }

    /// <summary>
    /// Sort key for a "rainbow shelf": chromatic colors by hue (red → violet),
    /// then near-greys from white to black at the end.
    /// </summary>
    public static double RainbowKey(string? hex)
    {
        if (!TryParseHex(hex, out var rgb))
        {
            return double.MaxValue;
        }

        var (h, s, l) = ToHsl(rgb);
        const double greyThreshold = 0.15;
        if (s < greyThreshold || l < 0.08 || l > 0.94)
        {
            return 1000 + (1 - l); // after all hues; light greys first, black last
        }

        return h;
    }

    /// <summary>
    /// CSS text-shadow that makes text look stamped/embossed: a dark drop under light text,
    /// a light highlight under dark text.
    /// </summary>
    public static string EmbossShadow(string? textHex)
    {
        if (!TryParseHex(textHex, out var c))
        {
            return "none";
        }

        var lum = ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255;
        return lum > 0.5
            ? "0 1px 1px rgb(0 0 0 / 0.55)"
            : "0 1px 0 rgb(255 255 255 / 0.35)";
    }

    /// <summary>Black or white, whichever has the higher contrast ratio on the given background.</summary>
    public static string ContrastText(string? backgroundHex)
    {
        if (!TryParseHex(backgroundHex, out _))
        {
            return LightText;
        }

        return ContrastRatio(LightText, backgroundHex) >= ContrastRatio(DarkText, backgroundHex) ? LightText : DarkText;
    }

    /// <summary>
    /// The lettering color for a spine or cover tile: the user's choice when it's readable on the background,
    /// otherwise black or white by contrast; ornate (leather) spines get gilt where gilt stands out well enough.
    /// </summary>
    public static string SpineTextColor(string? backgroundHex, string? customHex, bool ornate)
    {
        if (customHex is not null && ContrastRatio(customHex, backgroundHex) >= MinimumCustomContrast)
        {
            return customHex;
        }

        var automatic = ContrastText(backgroundHex);
        return ornate && automatic == LightText && ContrastRatio(Gilt, backgroundHex) >= ComfortableContrast
            ? Gilt
            : automatic;
    }

    /// <summary>WCAG contrast ratio, 1 (none) to 21 (black on white). Unparseable colors count as fully readable.</summary>
    public static double ContrastRatio(string? foregroundHex, string? backgroundHex)
    {
        if (!TryParseHex(foregroundHex, out var fg) || !TryParseHex(backgroundHex, out var bg))
        {
            return 21;
        }

        var a = RelativeLuminance(fg);
        var b = RelativeLuminance(bg);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public static double RelativeLuminance((byte R, byte G, byte B) c)
        => (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));

    private static double Linear(byte channel)
    {
        var v = channel / 255d;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
