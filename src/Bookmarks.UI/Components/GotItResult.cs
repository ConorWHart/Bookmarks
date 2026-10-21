namespace Bookmarks.UI.Components;

/// <summary>What <see cref="GotItDialog"/> returns.</summary>
/// <param name="PricePaid">Rounded to cents; null when left blank.</param>
/// <param name="PutOnShelf">Open the shelf picker for the book afterwards.</param>
public sealed record GotItResult(DateOnly AcquiredOn, decimal? PricePaid, bool PutOnShelf);
