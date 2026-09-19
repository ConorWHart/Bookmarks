using MudBlazor;

namespace Bookmarks.UI.Layout;

/// <summary>
/// "Reading room" palette: warm linen background, paper surfaces, walnut and
/// library-green accents, brass highlights. Light mode avoids pure white.
/// </summary>
public static class BookmarksTheme
{
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#7A4E2D",            // walnut
            PrimaryContrastText = "#FFF8EE",
            Secondary = "#2E5E4E",          // library green
            SecondaryContrastText = "#F3F7F4",
            Tertiary = "#B8934A",           // brass
            AppbarBackground = "#33241A",
            AppbarText = "#F3EBDD",
            Background = "#E9E1D3",         // linen: the page itself
            BackgroundGray = "#DED4C3",
            Surface = "#F7F2E8",            // paper: cards, toolbar, drawers
            DrawerBackground = "#EFE7DA",
            DrawerText = "#3B2A1E",
            DrawerIcon = "#7A4E2D",
            TextPrimary = "#2E2319",
            TextSecondary = "#6B5A49",
            TextDisabled = "#A89886",
            ActionDefault = "#6B5A49",
            ActionDisabled = "#B4A794",
            LinesDefault = "#D6CBB8",
            LinesInputs = "#B9AA95",
            TableLines = "#DDD3C2",
            Divider = "#D6CBB8",
            OverlayLight = "rgba(233,225,211,0.6)",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#D4A373",
            PrimaryContrastText = "#1E1611",
            Secondary = "#7FB7A4",
            Tertiary = "#D9B56A",
            AppbarBackground = "#1B1612",
            AppbarText = "#EDE3D4",
            Background = "#151210",
            BackgroundGray = "#1C1815",
            Surface = "#211C18",
            DrawerBackground = "#1B1612",
            DrawerText = "#E4D8C6",
            DrawerIcon = "#D4A373",
            TextPrimary = "#EDE3D4",
            TextSecondary = "#B5A690",
            LinesDefault = "#3A322B",
            LinesInputs = "#57493D",
            Divider = "#3A322B",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["Inter", "Segoe UI", "Roboto", "sans-serif"] },
            H1 = new H1Typography { FontFamily = ["Georgia", "serif"] },
            H2 = new H2Typography { FontFamily = ["Georgia", "serif"] },
            H3 = new H3Typography { FontFamily = ["Georgia", "serif"] },
            H4 = new H4Typography { FontFamily = ["Georgia", "serif"] },
            H5 = new H5Typography { FontFamily = ["Georgia", "serif"] },
            H6 = new H6Typography { FontFamily = ["Georgia", "serif"] },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
        },
    };
}
