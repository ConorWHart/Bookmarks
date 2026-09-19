using Microsoft.JSInterop;
using MudBlazor;

namespace Bookmarks.UI.Services;

/// <summary>Shared dialog options: editors go full-screen on phones.</summary>
public static class DialogDefaults
{
    private const int PhoneMaxWidthPx = 600;

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
