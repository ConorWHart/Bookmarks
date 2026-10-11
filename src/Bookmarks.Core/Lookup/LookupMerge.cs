using System.Globalization;
using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Lookup;

public enum LookupApplyMode
{
    /// <summary>Only blank fields are filled. The default for a book that already has details.</summary>
    FillEmpty,

    /// <summary>Every field the lookup found replaces the current value (fields it didn't find are kept).</summary>
    ReplaceAll,

    /// <summary>
    /// The lookup is a different book (another ISBN): every book detail and the cover come from it, and details it
    /// doesn't have are cleared, so nothing of the previous book is left behind. "Your copy" fields are kept.
    /// </summary>
    ReplaceBook,
}

/// <summary>Applies a lookup result to the editor model.</summary>
public static class LookupMerge
{
    /// <summary>
    /// Merges <paramref name="result"/> into <paramref name="model"/> and returns the names of the fields that changed
    /// (for "Filled 6 fields" feedback).
    /// </summary>
    /// <remarks>
    /// Only book details change. "Your copy" fields are left alone, except the binding of a <b>new</b> book: an ISBN
    /// identifies an edition, so it describes the copy being added. A new cover clears the extracted cover color so
    /// the UI extracts it again.
    /// </remarks>
    public static IReadOnlyList<string> Apply(BookEditModel model, BookLookupResult result, LookupApplyMode mode, bool isNewBook)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(result);

        var changed = new List<string>();
        var clear = mode == LookupApplyMode.ReplaceBook;
        var replace = mode is LookupApplyMode.ReplaceAll or LookupApplyMode.ReplaceBook;

        void Text(string name, Func<string?> get, Action<string?> set, string? value)
        {
            var current = get();
            if (string.IsNullOrWhiteSpace(value))
            {
                // A different book: drop the previous book's detail. Title and ISBN are never blanked.
                if (clear && !string.IsNullOrWhiteSpace(current) && name is not ("Title" or "ISBN"))
                {
                    set(null);
                    changed.Add(name);
                }

                return;
            }

            if ((replace || string.IsNullOrWhiteSpace(current)) && !string.Equals(current?.Trim(), value.Trim(), StringComparison.Ordinal))
            {
                set(value.Trim());
                changed.Add(name);
            }
        }

        void Number<T>(string name, Func<T?> get, Action<T?> set, T? value)
            where T : struct
        {
            var current = get();
            if (value is null)
            {
                if (clear && current is not null)
                {
                    set(null);
                    changed.Add(name);
                }

                return;
            }

            if ((replace || current is null) && !Equals(current, value))
            {
                set(value);
                changed.Add(name);
            }
        }

        Text("ISBN", () => model.Isbn13, v => model.Isbn13 = v, result.Isbn13);
        Text("Title", () => model.Title, v => model.Title = v ?? "", result.Title);
        Text("Subtitle", () => model.Subtitle, v => model.Subtitle = v, result.Subtitle);
        Text("Authors", () => model.AuthorsText, v => model.AuthorsText = v, Join(result.Authors));
        Text("Publisher", () => model.Publisher, v => model.Publisher = v, result.Publisher);
        Number("Year", () => model.PublishedYear, v => model.PublishedYear = v, result.PublishedYear);
        Number("Pages", () => model.PageCount, v => model.PageCount = v, result.PageCount);
        Text("Genres", () => model.GenresText, v => model.GenresText = v, Join(result.Genres));

        // Series and number go together.
        if (clear && string.IsNullOrWhiteSpace(result.Series) && !string.IsNullOrWhiteSpace(model.Series))
        {
            model.Series = null;
            model.SeriesNumber = null;
            changed.Add("Series");
        }
        else if (!string.IsNullOrWhiteSpace(result.Series) && (replace || string.IsNullOrWhiteSpace(model.Series))
            && !(string.Equals(model.Series, result.Series, StringComparison.Ordinal) && model.SeriesNumber == result.SeriesNumber))
        {
            model.Series = result.Series;
            model.SeriesNumber = result.SeriesNumber;
            changed.Add("Series");
        }

        var oldCover = model.CoverUrl;
        Text("Cover", () => model.CoverUrl, v => model.CoverUrl = v, result.CoverUrl);
        if (!string.Equals(oldCover, model.CoverUrl, StringComparison.Ordinal))
        {
            model.DominantColor = null;
        }

        Number("Height", () => model.HeightMm, v => model.HeightMm = v, result.HeightMm);
        Number("Width", () => model.WidthMm, v => model.WidthMm = v, result.WidthMm);
        Number("Thickness", () => model.ThicknessMm, v => model.ThicknessMm = v, result.ThicknessMm);

        if ((isNewBook || clear) && result.Format is { } format && model.Format != format)
        {
            model.Format = format;
            changed.Add("Format");
        }

        if (changed.Count > 0 && (replace || model.Source == BookSource.Manual))
        {
            model.Source = result.Source;
        }

        return changed;
    }

    /// <summary>
    /// <see cref="LookupApplyMode.ReplaceBook"/> when the result has a different ISBN from the book the form currently
    /// shows (<paramref name="currentBookIsbn"/>: the ISBN the form was opened with or last filled from); otherwise
    /// <see cref="LookupApplyMode.FillEmpty"/>, so details the user typed in are kept.
    /// </summary>
    public static LookupApplyMode ModeFor(string? currentBookIsbn, BookLookupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Isbn.TryNormalize(currentBookIsbn, out var current)
            && Isbn.TryNormalize(result.Isbn13, out var found)
            && current != found
                ? LookupApplyMode.ReplaceBook
                : LookupApplyMode.FillEmpty;
    }

    /// <summary>"Filled title, authors and 4 more" style summary of <see cref="Apply"/>'s result.</summary>
    public static string Describe(IReadOnlyList<string> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        return changed.Count switch
        {
            0 => "Nothing new to fill in",
            1 => $"Updated {changed[0].ToLowerInvariant()}",
            2 => $"Updated {changed[0].ToLowerInvariant()} and {changed[1].ToLowerInvariant()}",
            _ => string.Create(CultureInfo.InvariantCulture, $"Updated {changed[0].ToLowerInvariant()}, {changed[1].ToLowerInvariant()} and {changed.Count - 2} more"),
        };
    }

    private static string? Join(IReadOnlyList<string> values) => values.Count == 0 ? null : string.Join(", ", values);
}
