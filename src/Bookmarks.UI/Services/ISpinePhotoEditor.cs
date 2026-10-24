using Microsoft.AspNetCore.Components;

namespace Bookmarks.UI.Services;

/// <summary>
/// Crops a photo of a book's spine and turns it into a small image kept with the book (Phase 7e). The PWA does it in
/// the browser (<c>js/spinePhoto.js</c> with Cropper.js); one photo is edited at a time.
/// </summary>
public interface ISpinePhotoEditor
{
    /// <summary>
    /// Shows the photo chosen in <paramref name="fileInput"/> in <paramref name="image"/>, ready to crop.
    /// <paramref name="aspect"/> is the spine's width ÷ height to keep the crop to, or 0 for a free crop.
    /// Returns null, or an error code: <c>nofile</c>, <c>notimage</c>, <c>unreadable</c>, <c>nocropper</c>, <c>unsupported</c>.
    /// </summary>
    Task<string?> LoadAsync(ElementReference fileInput, ElementReference image, double aspect);

    /// <summary>
    /// The same for an image on the web: the book's cover, to cut a spine from. Error codes: <c>blocked</c> (the site
    /// doesn't allow reading its images), <c>unreadable</c>, <c>nocropper</c>, <c>unsupported</c>.
    /// </summary>
    Task<string?> LoadUrlAsync(string url, ElementReference image, double aspect);

    Task RotateAsync(int degrees);

    Task SetAspectAsync(double aspect);

    /// <summary>The cropped photo, downscaled and encoded; null when nothing is loaded or it can't be made small enough.</summary>
    Task<SpinePhoto?> ExportAsync();

    /// <summary>Stops cropping and frees the photo.</summary>
    Task CloseAsync();
}

/// <summary>A finished spine photo.</summary>
/// <param name="DataUrl">A <c>data:image/webp</c> (or jpeg) URL, stored in <c>SpineAppearance.SpineImageUrl</c>.</param>
/// <param name="Color">Its dominant colour (#RRGGBB), used as the spine colour; null when none stands out.</param>
public sealed record SpinePhoto(string DataUrl, string? Color, int Width, int Height)
{
    /// <summary>Longest side of a stored spine photo, in pixels: sharp on a zoomed-in shelf on a phone, still small.</summary>
    public const int MaxLongEdge = 720;

    /// <summary>Most characters a stored photo may take (it syncs with the book).</summary>
    public const int MaxChars = 48_000;

    /// <summary>A stored spine image that can be shown: a data URL of an image, or an https link.</summary>
    public static bool IsUsable(string? url)
        => url is not null
            && (url.StartsWith("data:image/", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Where there's no browser to crop in.</summary>
public sealed class NullSpinePhotoEditor : ISpinePhotoEditor
{
    public Task<string?> LoadAsync(ElementReference fileInput, ElementReference image, double aspect) => Task.FromResult<string?>("unsupported");

    public Task<string?> LoadUrlAsync(string url, ElementReference image, double aspect) => Task.FromResult<string?>("unsupported");

    public Task RotateAsync(int degrees) => Task.CompletedTask;

    public Task SetAspectAsync(double aspect) => Task.CompletedTask;

    public Task<SpinePhoto?> ExportAsync() => Task.FromResult<SpinePhoto?>(null);

    public Task CloseAsync() => Task.CompletedTask;
}
