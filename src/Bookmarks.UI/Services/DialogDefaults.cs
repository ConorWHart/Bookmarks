using Microsoft.JSInterop;
using MudBlazor;

namespace Bookmarks.UI.Services;

/// <summary>Shared dialog options: editors go full-screen on phones.</summary>
public static class DialogDefaults
{
    private const int PhoneMaxWidthPx = 600;

    /// <summary>
    /// A bottom sheet (<c>MudDialog Class="bm-sheet"</c>, styled in app.css): full width on phones, sized to its content
    /// and growing upwards as it gets taller, scrolling inside once it reaches the top.
    /// </summary>
    /// <param name="backdropClick">Whether tapping outside closes it; off for sheets that hold more typing.</param>
    public static DialogOptions Sheet(bool backdropClick = true) => new()
    {
        Position = DialogPosition.BottomCenter,
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = false,
        BackdropClick = backdropClick,
    };

    public static async Task<DialogOptions> EditorAsync(IBrowserViewportService viewport, MaxWidth maxWidth = MaxWidth.Medium)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var fullScreen = false;
        try
        {
            fullScreen = (await viewport.GetCurrentBrowserWindowSizeAsync()).Width < PhoneMaxWidthPx;
        }
        catch (JSException)
        {
            // Fall back to a normal dialog.
        }

        return new DialogOptions
        {
            MaxWidth = maxWidth,
            FullWidth = true,
            FullScreen = fullScreen,
            CloseButton = true,
            BackdropClick = false,
        };
    }
}
