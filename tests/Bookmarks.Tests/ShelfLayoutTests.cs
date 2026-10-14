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
