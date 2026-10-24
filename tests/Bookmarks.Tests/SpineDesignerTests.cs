using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests;

public class SpineDesignerTests
{
    private static readonly string[] Dune = ["#D9822B", "#2B3A55", "#F2E3C6", "#7A2E1F"];

    [Fact]
    public void Every_design_uses_an_accent_style_and_readable_lettering()
    {
        for (var v = 0; v < SpineDesigner.VariantCount(Dune); v++)
        {
            var d = SpineDesigner.Design(Dune, v);

            Assert.True(SpineDesigner.UsesAccent(d.Style));
            Assert.NotEqual(d.SpineColor, d.AccentColor);
            Assert.True(ColorUtil.ContrastRatio(d.TextColor, d.SpineColor) >= ColorUtil.ComfortableContrast,
                $"variant {v}: {d.TextColor} on {d.SpineColor}");
        }
    }

    [Fact]
    public void Shuffling_changes_the_style_first_and_wraps_around()
    {
        var count = SpineDesigner.VariantCount(Dune);
        var styles = Enumerable.Range(0, SpineDesigner.Styles.Count).Select(v => SpineDesigner.Design(Dune, v).Style).ToList();

        Assert.Equal(SpineDesigner.Styles, styles);
        Assert.Equal(SpineDesigner.Design(Dune, 1), SpineDesigner.Design(Dune, count + 1));
    }

    [Fact]
    public void A_one_colour_cover_still_gets_a_contrasting_accent()
    {
        var d = SpineDesigner.Design(["#1F3A5F"], 0);

        Assert.Equal("#1F3A5F", d.SpineColor);
        Assert.True(ColorUtil.ContrastRatio(d.AccentColor, d.SpineColor) >= 2, d.AccentColor);
        Assert.Equal(SpineDesigner.Styles.Count, SpineDesigner.VariantCount(["#1F3A5F"]));
    }

    [Fact]
    public void No_colours_falls_back_rather_than_failing()
    {
        var d = SpineDesigner.Design([], 3);

        Assert.StartsWith("#", d.SpineColor, StringComparison.Ordinal);
        Assert.Equal(SpineStyle.TwoTone, d.Style);
    }

    [Fact]
    public void Colours_too_close_together_are_not_paired()
    {
        // Two near-identical blues: only the complementary pairings remain.
        Assert.Equal(SpineDesigner.Styles.Count * 2, SpineDesigner.VariantCount(["#1F3A5F", "#22406A"]));
    }

    [Fact]
    public void The_first_design_is_stable_per_book()
    {
        var id = Guid.Parse("0192f0c4-5d6e-7a8b-9c0d-1e2f3a4b5c6d");
        var count = SpineDesigner.VariantCount(Dune);

        Assert.Equal(SpineDesigner.StartVariant(id, count), SpineDesigner.StartVariant(id, count));
        Assert.InRange(SpineDesigner.StartVariant(id, count), 0, count - 1);
    }
}
