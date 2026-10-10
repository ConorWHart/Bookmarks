using System.Diagnostics.CodeAnalysis;

namespace Bookmarks.Core.Lookup;

/// <summary>
/// ISBN parsing and validation. Everything is stored as ISBN-13 (what barcodes encode); ISBN-10s are converted.
/// </summary>
public static class Isbn
{
    /// <summary>
    /// Accepts an ISBN-13 (978/979) or ISBN-10 with any hyphens, spaces or an "ISBN" / "ISBN-13:" prefix,
    /// and returns the 13-digit form when the checksum is valid.
    /// </summary>
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? isbn13)
    {
        isbn13 = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        if (text.StartsWith("ISBN", StringComparison.OrdinalIgnoreCase))
        {
            text = text[4..];
            if (text.StartsWith("-13", StringComparison.Ordinal) || text.StartsWith("-10", StringComparison.Ordinal))
            {
                text = text[3..];
            }
        }

        Span<char> digits = stackalloc char[13];
        var n = 0;
        foreach (var ch in text)
        {
            if (ch is '-' or ' ' or ':' or '‐' or '‑' or '‒' or '–' or ' ')
            {
                continue;
            }

            if (n == 13 || !(char.IsAsciiDigit(ch) || ch is 'X' or 'x'))
            {
                return false;
            }

            digits[n++] = char.ToUpperInvariant(ch);
        }

        var s = new string(digits[..n]);
        if (n == 13 && IsValid13(s))
        {
            isbn13 = s;
            return true;
        }

        if (n == 10 && IsValid10(s))
        {
            isbn13 = From10(s);
            return true;
        }

        return false;
    }

    /// <summary>True for a 13-digit Bookland EAN (978/979) with a correct check digit.</summary>
    public static bool IsValid13(string? s)
    {
        if (s is not { Length: 13 } || !s.All(char.IsAsciiDigit) || !(s.StartsWith("978", StringComparison.Ordinal) || s.StartsWith("979", StringComparison.Ordinal)))
        {
            return false;
        }

        return s[12] - '0' == CheckDigit13(s);
    }

    /// <summary>True for a 10-character ISBN (last character may be X) with a correct check digit.</summary>
    public static bool IsValid10(string? s)
    {
        if (s is not { Length: 10 } || !s[..9].All(char.IsAsciiDigit) || !(char.IsAsciiDigit(s[9]) || s[9] is 'X' or 'x'))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var d = s[i] is 'X' or 'x' ? 10 : s[i] - '0';
            sum += (10 - i) * d;
        }

        return sum % 11 == 0;
    }

    /// <summary>Converts a valid ISBN-10 to ISBN-13 (978 prefix).</summary>
    public static string From10(string isbn10)
    {
        ArgumentNullException.ThrowIfNull(isbn10);
        var body = "978" + isbn10[..9];
        return body + (char)('0' + CheckDigit13(body));
    }

    /// <summary>The ISBN-10 for a 978 ISBN-13, or null (979 numbers have no ISBN-10).</summary>
    public static string? To10(string isbn13)
    {
        if (!IsValid13(isbn13) || !isbn13.StartsWith("978", StringComparison.Ordinal))
        {
            return null;
        }

        var body = isbn13[3..12];
        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            sum += (10 - i) * (body[i] - '0');
        }

        var check = (11 - (sum % 11)) % 11;
        return body + (check == 10 ? "X" : ((char)('0' + check)).ToString());
    }

    private static int CheckDigit13(string s)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (s[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (10 - (sum % 10)) % 10;
    }
}
