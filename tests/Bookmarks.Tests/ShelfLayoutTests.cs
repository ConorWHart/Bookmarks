using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;
using static Bookmarks.Tests.TestData;

namespace Bookmarks.Tests;

public class ShelfLayoutTests
{
    [Fact]
    public void Thickness_is_estimated_from_page_count_and_clamped()
    {
        Assert.Equal(6, SpineMetrics.ThicknessMm(Entry("Thin", pages: 20)));
        Assert.Equal(90, SpineMetrics.ThicknessMm(Entry("Huge", pages: 5_000)));

        var hardcover = SpineMetrics.ThicknessMm(Entry("HC", pages: 400, format: BookFormat.Hardcover));
        var paperback = SpineMetrics.ThicknessMm(Entry("PB", pages: 400, format: BookFormat.Paperback));
        Assert.True(hardcover > paperback);
    }

    [Fact]
    public void Thicker_paper_makes_a_thicker_spine()
    {
        var standard = Entry("Standard", pages: 400);
        var thick = Entry("Thick", pages: 400);
        thick.Item.Appearance.PaperThickness = PaperThickness.Thick;
        var veryThick = Entry("Very thick", pages: 400);
        veryThick.Item.Appearance.PaperThickness = PaperThickness.VeryThick;

        var thin = Entry("Thin", pages: 400);
        thin.Item.Appearance.PaperThickness = PaperThickness.Thin;
        var veryThin = Entry("Very thin", pages: 400);
        veryThin.Item.Appearance.PaperThickness = PaperThickness.VeryThin;

        Assert.Equal(15, SpineMetrics.ThicknessMm(veryThin));   // 400 × 0.035 + 1
        Assert.Equal(21, SpineMetrics.ThicknessMm(thin));       // 400 × 0.05 + 1
        Assert.Equal(27, SpineMetrics.ThicknessMm(standard));   // 400 × 0.065 + 1
        Assert.Equal(37, SpineMetrics.ThicknessMm(thick));      // 400 × 0.09 + 1
        Assert.Equal(49, SpineMetrics.ThicknessMm(veryThick));  // 400 × 0.12 + 1
    }

    [Fact]
    public void Automatic_paper_uses_a_measured_thickness_but_a_chosen_paper_overrides_it()
    {
        var entry = Entry("Measured", pages: 400);
        entry.Book.ThicknessMm = 22;
        Assert.Equal(22, SpineMetrics.ThicknessMm(entry)); // automatic

        entry.Item.Appearance.PaperThickness = PaperThickness.Thick;
        Assert.Equal(37, SpineMetrics.ThicknessMm(entry)); // chosen: page count × thick paper
    }

    [Fact]
    public void A_chosen_height_wins_over_the_format()
    {
        var entry = Entry("Atlas", format: BookFormat.Hardcover);
        entry.Item.Appearance.HeightMmOverride = BookSizes.Common.Single(s => s.Name == "Quarto").HeightMm;

        Assert.Equal(305, SpineMetrics.HeightMm(entry));
    }

    [Fact]
    public void Height_falls_back_to_format_defaults()
    {
        Assert.Equal(240, SpineMetrics.HeightMm(Entry("HC", format: BookFormat.Hardcover)));
        Assert.Equal(175, SpineMetrics.HeightMm(Entry("MM", format: BookFormat.MassMarketPaperback)));
    }

    [Fact]
    public void Flow_wraps_books_onto_new_shelves_without_exceeding_width()
    {
        var entries = Enumerable.Range(0, 40).Select(i => Entry($"Book {i}", pages: 300)).ToList();

        var rows = ShelfLayoutEngine.Flow(entries, shelfWidthMm: 200, gapMm: 1);

        Assert.True(rows.Count > 1);
        Assert.All(rows, r => Assert.True(r.UsedWidthMm <= 200));
        Assert.Equal(40, rows.Sum(r => r.Books.Count));

        // Order is preserved across rows.
        var flattened = rows.SelectMany(r => r.Books).Select(b => b.Entry.Title);
        Assert.Equal(entries.Select(e => e.Title), flattened);
    }

    [Fact]
    public void Face_out_can_be_decided_per_shelf()
    {
        var book = Entry("Circe", pages: 393);

        var spine = Assert.Single(Assert.Single(ShelfLayoutEngine.Flow([book], 900)).Books);
        var cover = Assert.Single(Assert.Single(ShelfLayoutEngine.Flow([book], 900, isFaceOut: _ => true)).Books);

        Assert.False(spine.FaceOut);
        Assert.True(cover.FaceOut);
        Assert.True(cover.WidthMm > spine.WidthMm);
    }

    [Fact]
    public void Flow_of_nothing_is_no_rows()
        => Assert.Empty(ShelfLayoutEngine.Flow([], 500));
}
