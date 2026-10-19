using Bookmarks.Core.Library;
using Bookmarks.Core.Lookup;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests;

public class ColorUtilTests
{
    [Theory]
    [InlineData("#8998B7", ColorUtil.DarkText)] // slate blue: the old brightness cut-off picked white (2.9:1) over black (6:1)
    [InlineData("#C8A928", ColorUtil.DarkText)] // gold cover color
    [InlineData("#EBA755", ColorUtil.DarkText)] // orange cover color
    [InlineData("#1F3A5F", ColorUtil.LightText)] // navy
    [InlineData("#3B7EA1", ColorUtil.LightText)] // mid blue: white 4.5:1 just beats black 3.9:1
    [InlineData("#FFFFFF", ColorUtil.DarkText)]
    [InlineData("#000000", ColorUtil.LightText)]
    [InlineData("not a color", ColorUtil.LightText)]
    public void Automatic_text_takes_the_higher_contrast(string background, string expected)
        => Assert.Equal(expected, ColorUtil.ContrastText(background));

    [Fact]
    public void Contrast_ratio_matches_wcag()
    {
        Assert.Equal(21, ColorUtil.ContrastRatio("#000000", "#FFFFFF"), 2);
        Assert.Equal(1, ColorUtil.ContrastRatio("#8C2F1B", "#8C2F1B"), 2);
        Assert.Equal(ColorUtil.ContrastRatio("#1F3A5F", "#FFFFFF"), ColorUtil.ContrastRatio("#FFFFFF", "#1F3A5F"), 6);
    }

    [Fact]
    public void A_readable_custom_text_color_is_kept()
        => Assert.Equal("#E9C46A", ColorUtil.SpineTextColor("#1F3A5F", "#E9C46A", ornate: false));

    [Fact]
    public void An_unreadable_custom_text_color_falls_back_to_automatic()
        => Assert.Equal(ColorUtil.DarkText, ColorUtil.SpineTextColor("#7FB7BE", "#E9C46A", ornate: false)); // gilt on teal, 1.3:1

    [Fact]
    public void Ornate_spines_get_gilt_only_where_it_stands_out()
    {
        Assert.Equal(ColorUtil.Gilt, ColorUtil.SpineTextColor("#6E2C2C", null, ornate: true));      // oxblood: gilt 6.1:1
        Assert.Equal(ColorUtil.LightText, ColorUtil.SpineTextColor("#3B7EA1", null, ornate: true)); // mid blue: gilt only 2.7:1
        Assert.Equal(ColorUtil.DarkText, ColorUtil.SpineTextColor("#C8A928", null, ornate: true));  // light spine: never gilt
    }

    [Fact]
    public void A_cover_color_resets_a_clashing_text_color()
    {
        var model = BookEditModel.ForNew();
        model.Title = "X";
        model.TextColor = "#F4EBD9"; // cream, fine on the random dark swatches

        model.UseCoverColor("#C8A928"); // gold: cream would be 1.9:1

        Assert.Equal("#C8A928", model.SpineColor);
        Assert.Null(model.TextColor);
    }

    [Fact]
    public void A_cover_color_keeps_a_readable_text_color()
    {
        var model = BookEditModel.ForNew();
        model.Title = "X";
        model.TextColor = "#F4EBD9";

        model.UseCoverColor("#1F3A5F"); // navy

        Assert.Equal("#F4EBD9", model.TextColor);
    }

    [Fact]
    public void Backfill_spine_color_also_resets_a_clashing_text_color()
    {
        var book = new Book { Title = "X", CoverUrl = "https://covers.openlibrary.org/b/id/1-M.jpg" };
        var item = new LibraryItem
        {
            BookId = book.Id,
            Appearance = new SpineAppearance { SpineColor = "#1F3A5F", TextColor = "#F4EBD9" },
        };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));
        model.UseCoverColor("#C8A928");

        Assert.True(CoverBackfill.UseCoverColorForSpine(model));
        Assert.Equal("#C8A928", model.SpineColor);
        Assert.Null(model.TextColor);
    }

    [Theory]
    [InlineData("#000000", 0.5, "#808080")]
    [InlineData("#ffffff", -0.5, "#808080")]
    [InlineData("#A8743F", 0, "#A8743F")]
    [InlineData("#204060", 1, "#FFFFFF")]
    public void Shade_mixes_toward_white_or_black(string hex, double amount, string expected)
        => Assert.Equal(expected, ColorUtil.Shade(hex, amount));

    [Fact]
    public void A_custom_bookcase_colour_gets_a_lighter_highlight_and_darker_shadow_and_back()
    {
        var palette = ColorUtil.BookcasePaletteFor("#2F5D50")!;

        Assert.Equal("#2F5D50", palette.Wood);
        Assert.True(Lum(palette.Highlight) > Lum(palette.Wood));
        Assert.True(Lum(palette.Shadow) < Lum(palette.Wood));
        Assert.True(Lum(palette.Back) < Lum(palette.Shadow)); // a dark case: deep back panel, light label ink
        Assert.StartsWith("rgba(255", palette.Label);

        var light = ColorUtil.BookcasePaletteFor("#E8DCC8")!;
        Assert.True(Lum(light.Back) > Lum(light.Shadow)); // a light case: back only a little darker, dark ink
        Assert.StartsWith("rgba(40", light.Label);

        Assert.Null(ColorUtil.BookcasePaletteFor("not a colour"));
    }

    private static double Lum(string hex)
        => ColorUtil.TryParseHex(hex, out var c) ? ColorUtil.RelativeLuminance(c) : -1;
}
