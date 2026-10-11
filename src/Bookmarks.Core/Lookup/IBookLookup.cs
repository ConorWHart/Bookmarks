namespace Bookmarks.Core.Lookup;

/// <summary>
/// Finds book details online. Open Library today; another source (Google Books via our API) can be added
/// behind this interface later.
/// </summary>
/// <remarks>
/// "Not found" is a null / empty result. Network and server failures are thrown (<see cref="HttpRequestException"/>,
/// <see cref="TaskCanceledException"/> on timeout) so the UI can say "couldn't reach Open Library" instead of "not found".
/// </remarks>
public interface IBookLookup
{
    /// <summary>Looks up one edition. <paramref name="isbn"/> may be ISBN-10 or -13, with or without hyphens.</summary>
    /// <exception cref="ArgumentException">The ISBN isn't valid (checked before any network call).</exception>
    Task<BookLookupResult?> FindByIsbnAsync(string isbn, CancellationToken ct = default);

    /// <summary>Free-text title / author search, best matches first.</summary>
    Task<IReadOnlyList<BookSearchHit>> SearchAsync(string query, int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Cover images to choose from for a book (medium-size URLs, best first): this edition's, the work's, then other
    /// editions'. Found by ISBN, or by title and author when there's no ISBN. Empty when nothing is found.
    /// </summary>
    Task<IReadOnlyList<string>> FindCoversAsync(string? isbn, string? title, string? author, CancellationToken ct = default);
}
