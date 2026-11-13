using Microsoft.AspNetCore.Components;

namespace Bookmarks.UI.Services;

/// <summary>
/// Reads a cover image the user copied ("Copy image" on a phone, or Ctrl+V on a computer) and turns it into a small
/// image kept with the book, like spine photos (Phase 7e). Needed because Chrome on a phone shares Google image
/// results as a link to the results page, which isn't an image. The PWA does it in the browser (<c>js/coverPhoto.js</c>).
/// </summary>
public interface ICoverPhotoReader
{
    /// <summary>Reads the image on the clipboard. Call it from a tap or click: browsers allow it only then.</summary>
    Task<PastedCover> ReadClipboardAsync();

    /// <summary>
    /// Calls <paramref name="onPasted"/> for images pasted inside <paramref name="area"/> (text pastes are left alone),
    /// until the returned handle is disposed.
    /// </summary>
    Task<IAsyncDisposable> ListenForPasteAsync(ElementReference area, Func<PastedCover, Task> onPasted);
}

/// <summary>A pasted cover: <see cref="DataUrl"/>, or an <see cref="Error"/> code when there's none.</summary>
/// <param name="Error">
/// <c>unsupported</c> (this browser can't read the clipboard from a button), <c>denied</c>, <c>noimage</c>,
/// <c>unreadable</c> or <c>toobig</c>.
/// </param>
public sealed record PastedCover(string? DataUrl, string? Error)
{
    /// <summary>Longest side of a stored cover, in pixels: sharp in the details drawer, still small.</summary>
    public const int MaxLongEdge = 600;

    /// <summary>Most characters a stored cover may take (it syncs with the book).</summary>
    public const int MaxChars = 60_000;

    public static PastedCover Failed(string error) => new(null, error);

    /// <summary>What to tell the user when there's no image.</summary>
    public string? Message => Error switch
    {
        null => null,
        "unsupported" => "This browser can't paste from a button. Press and hold the link box and choose Paste.",
        "denied" => "Bookmarks wasn't allowed to read the clipboard. Allow it in the browser's site settings, or press and hold the link box and choose Paste.",
        "noimage" => "There's no image on the clipboard. Press and hold the picture, choose Copy image, then try again.",
        "toobig" => "That image is too large to keep with the book.",
        _ => "That image couldn't be read. Try copying it again, or use another picture.",
    };
}

/// <summary>Where there's no browser.</summary>
public sealed class NullCoverPhotoReader : ICoverPhotoReader
{
    public Task<PastedCover> ReadClipboardAsync() => Task.FromResult(PastedCover.Failed("unsupported"));

    public Task<IAsyncDisposable> ListenForPasteAsync(ElementReference area, Func<PastedCover, Task> onPasted)
        => Task.FromResult<IAsyncDisposable>(new Nothing());

    private sealed class Nothing : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
