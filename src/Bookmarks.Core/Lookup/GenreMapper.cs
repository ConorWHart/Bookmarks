using System.Text.RegularExpressions;

namespace Bookmarks.Core.Lookup;

/// <summary>
/// Turns noisy catalog subjects ("Arkenstone", "nyt:hardcover-fiction=2021-05-23", "Fiction, fantasy, epic")
/// into at most a few genres from a small, consistent vocabulary, so genre filters stay useful.
/// </summary>
public static class GenreMapper
{
    public const int MaxGenres = 3;

    // Order matters: each subject takes the first rule it matches, so specific rules come before broad ones
    // ("science fiction" before "science", "historical fiction" before "history").
    private static readonly (string Genre, Regex Pattern)[] Rules =
    [
        Rule("Science Fiction", "science fiction", "science-fiction", "sci-fi", "scifi", "space opera", "cyberpunk"),
        Rule("Fantasy", "fantasy", "fantasies"),
        Rule("Horror", "horror", "ghost stories"),
        Rule("Mystery", "mystery", "mysteries", "detective", "detective and mystery stories", "crime fiction", "whodunit"),
        Rule("Thriller", "thriller", "thrillers", "suspense"),
        Rule("Romance", "romance", "love stories"),
        Rule("Historical Fiction", "historical fiction"),
        Rule("Graphic Novels", "comics", "comic books", "graphic novels", "graphic novel", "manga"),
        Rule("Young Adult", "young adult", "teen"),
        Rule("Children's", "juvenile fiction", "juvenile literature", "children's fiction", "children's books", "picture books"),
        Rule("Poetry", "poetry", "poems"),
        Rule("Humor", "humor", "humour", "humorous"),
        Rule("Classics", "classics", "classic literature"),
        Rule("Literary Fiction", "literary fiction"),
        Rule("Biography", "biography", "autobiography", "memoir", "memoirs"),
        Rule("History", "history"),
        Rule("Science", "science", "physics", "biology", "chemistry", "astronomy", "mathematics"),
        Rule("Philosophy", "philosophy"),
        Rule("Psychology", "psychology"),
        Rule("Religion", "religion", "theology", "christianity", "spirituality"),
        Rule("Self-Help", "self-help", "self help", "personal development"),
        Rule("Business", "business", "economics", "management", "entrepreneurship"),
        Rule("Politics", "politics", "political science"),
        Rule("Travel", "travel"),
        Rule("Cooking", "cooking", "cookery", "cookbooks", "recipes"),
        Rule("Art", "art", "painting", "photography", "design"),
    ];

    private static readonly Regex FictionOnly = new(@"^\s*fiction\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> FromSubjects(IEnumerable<string>? subjects)
    {
        var genres = new List<string>(MaxGenres);
        var sawFiction = false;
        foreach (var subject in subjects ?? [])
        {
            // Machine tags such as "nyt:hardcover-fiction=2021-05-23" or "series:Discworld" aren't genres.
            if (string.IsNullOrWhiteSpace(subject) || subject.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            sawFiction |= FictionOnly.IsMatch(subject);
            foreach (var (genre, pattern) in Rules)
            {
                if (pattern.IsMatch(subject))
                {
                    if (!genres.Contains(genre))
                    {
                        genres.Add(genre);
                    }

                    break;
                }
            }

            if (genres.Count == MaxGenres)
            {
                break;
            }
        }

        if (genres.Count == 0 && sawFiction)
        {
            genres.Add("Fiction");
        }

        return genres;
    }

    private static (string, Regex) Rule(string genre, params string[] keywords)
        => (genre, new Regex(
            @"(?<![\w-])(?:" + string.Join('|', keywords.Select(Regex.Escape)) + @")(?![\w-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
}
