namespace Bookmarks.Core.Ranking;

/// <summary>
/// Fractional-index keys for user-defined ordering (books on a shelf, shelves in a bookcase).
/// Moving an item only rewrites that item's key, so a drag-and-drop is one synced row
/// instead of renumbering everything after it.
/// </summary>
/// <remarks>
/// Keys are base-62 strings compared with ordinal comparison. A key never ends in '0',
/// which guarantees there is always room for another key between any two keys.
/// Based on the algorithm described by David Greenspan / rocicorp "fractional-indexing".
/// </remarks>
public static class RankKey
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>Returns a key that sorts strictly between <paramref name="before"/> and <paramref name="after"/>.
    /// Pass null for "start of list" / "end of list".</summary>
    public static string Between(string? before, string? after)
    {
        var a = before ?? "";
        if (after is not null && string.CompareOrdinal(a, after) >= 0)
        {
            throw new ArgumentException($"'{before}' must sort before '{after}'.");
        }

        if (a.EndsWith('0') || (after?.EndsWith('0') ?? false))
        {
            throw new ArgumentException("Rank keys must not end with '0'.");
        }

        return Midpoint(a, after);
    }

    /// <summary>Generates <paramref name="count"/> evenly spread, ascending keys (e.g. for an initial import).</summary>
    public static IReadOnlyList<string> Sequence(int count)
    {
        var keys = new List<string>(count);
        string? last = null;
        for (var i = 0; i < count; i++)
        {
            last = Between(last, null);
            keys.Add(last);
        }

        return keys;
    }

    public static int Compare(string? x, string? y) => string.CompareOrdinal(x, y);

    private static string Midpoint(string a, string? b)
    {
        if (b is not null)
        {
            // Skip the common prefix (a is treated as padded with '0').
            var n = 0;
            while ((n < a.Length ? a[n] : '0') == b[n])
            {
                n++;
            }

            if (n > 0)
            {
                return b[..n] + Midpoint(n < a.Length ? a[n..] : "", b[n..]);
            }
        }

        // First digits differ.
        var digitA = a.Length > 0 ? Digits.IndexOf(a[0], StringComparison.Ordinal) : 0;
        var digitB = b is not null ? Digits.IndexOf(b[0], StringComparison.Ordinal) : Digits.Length;

        if (digitB - digitA > 1)
        {
            var mid = (int)Math.Round(0.5 * (digitA + digitB));
            return Digits[mid].ToString();
        }

        // Digits are consecutive.
        if (b is not null && b.Length > 1)
        {
            return b[..1];
        }

        return Digits[digitA] + Midpoint(a.Length > 0 ? a[1..] : "", null);
    }
}
