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
        var replace = mode == LookupApplyMode.ReplaceAll;

        void Text(string name, Func<string?> get, Action<string?> set, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var current = get();
            if ((replace || string.IsNullOrWhiteSpace(current)) && !string.Equals(current?.Trim(), value.Trim(), StringComparison.Ordinal))
            {
                set(value.Trim());
                changed.Add(name);
            }
        }

        void Number<T>(string name, Func<T?> get, Action<T?> set, T? value)
            where T : struct
        {
            if (value is null)
            {
                return;
            }

            var current = get();
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
        if (!string.IsNullOrWhiteSpace(result.Series) && (replace || string.IsNullOrWhiteSpace(model.Series))
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

        if (isNewBook && result.Format is { } format && model.Format != format)
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
