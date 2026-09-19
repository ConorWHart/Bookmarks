using System.Globalization;

namespace Bookmarks.Core.Library;

public static class ColorUtil
{
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

    /// <summary>Black or white, whichever reads better on the given background.</summary>
    public static string ContrastText(string? backgroundHex)
    {
        if (!TryParseHex(backgroundHex, out var c))
        {
            return "#FFFFFF";
        }

        // Relative luminance (sRGB approximation is fine for this purpose).
        var lum = ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) / 255;
        return lum > 0.6 ? "#1A1A1A" : "#FFFFFF";
    }
}
