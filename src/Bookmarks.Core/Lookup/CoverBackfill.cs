using Bookmarks.Core.Library;

namespace Bookmarks.Core.Lookup;

/// <summary>Which books "Find covers &amp; details" will work on.</summary>
/// <param name="Lookups">Books with a valid ISBN that are missing a cover or basic details: looked up on Open Library.</param>
/// <param name="ColorOnly">Books that have a cover but no extracted color yet: no lookup needed.</param>
/// <param name="WithoutIsbn">Books missing details that can't be looked up because they have no valid ISBN.</param>
public sealed record BackfillPlan(IReadOnlyList<LibraryEntry> Lookups, IReadOnlyList<LibraryEntry> ColorOnly, int WithoutIsbn)
{
    public int Total => Lookups.Count + ColorOnly.Count;
}

/// <summary>Planning and per-book rules for filling in covers and details of books already in the library.</summary>
public static class CoverBackfill
{
    /// <summary>Open Library asks unidentified apps for ≤ 1 request per second; an ISBN lookup makes two.</summary>
    public static readonly TimeSpan MinTimePerLookup = TimeSpan.FromSeconds(2);

    public static BackfillPlan Plan(IEnumerable<LibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var lookups = new List<LibraryEntry>();
        var colorOnly = new List<LibraryEntry>();
        var withoutIsbn = 0;

        // Several copies can share a book record; each book is handled once.
        foreach (var entry in entries.DistinctBy(e => e.Book.Id))
        {
            if (NeedsDetails(entry))
            {
                if (Isbn.TryNormalize(entry.Book.Isbn13, out _))
                {
                    lookups.Add(entry);
                }
                else
                {
                    withoutIsbn++;
                    if (NeedsColor(entry))
                    {
                        colorOnly.Add(entry);
                    }
                }
            }
            else if (NeedsColor(entry))
            {
                colorOnly.Add(entry);
            }
        }

        return new BackfillPlan(lookups, colorOnly, withoutIsbn);
    }

    /// <summary>No cover, no page count or no author: worth a lookup.</summary>
    public static bool NeedsDetails(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var book = entry.Book;
        return string.IsNullOrWhiteSpace(book.CoverUrl) || book.PageCount is null || book.Authors.Count == 0;
    }

    public static bool NeedsColor(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return !string.IsNullOrWhiteSpace(entry.Book.CoverUrl) && string.IsNullOrWhiteSpace(entry.Book.DominantColor);
    }

    /// <summary>
    /// Makes the spine follow the cover color, replacing a stored spine color. Returns true when that changes anything.
    /// </summary>
    /// <remarks>
    /// Books added before Phase 5 store their random starting color as an explicit spine color, so without this a
    /// cover found later would never show on the shelf.
    /// </remarks>
    public static bool UseCoverColorForSpine(BookEditModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.DominantColor is not { } color)
        {
            return false;
        }

        var textBefore = model.TextColor;
        var changed = !model.SpineColorFollowsCover || !string.Equals(model.SpineColor, color, StringComparison.OrdinalIgnoreCase);
        model.SpineColor = color;
        model.SpineColorFollowsCover = true;
        model.EnsureReadableText();
        return changed || model.TextColor != textBefore;
    }

    /// <summary>"about 40 seconds", "about 3 minutes".</summary>
    public static string DescribeDuration(int lookups)
    {
        var seconds = (int)Math.Ceiling(lookups * MinTimePerLookup.TotalSeconds);
        return seconds switch
        {
            < 60 => $"about {Math.Max(seconds, 5)} seconds",
            < 90 => "about a minute",
            _ => $"about {(int)Math.Round(seconds / 60.0)} minutes",
        };
    }
}
