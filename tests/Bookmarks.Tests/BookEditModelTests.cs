using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests;

public class BookEditModelTests
{
    [Fact]
    public void SplitList_trims_drops_empties_and_duplicates()
        => Assert.Equal(new[] { "Terry Pratchett", "Neil Gaiman" }, BookEditModel.SplitList(" Terry Pratchett, ,Neil Gaiman; terry pratchett ").ToArray());

    [Fact]
    public void ApplyTo_writes_book_and_copy()
    {
        var model = new BookEditModel
        {
            Isbn13 = "978-0-261-10221-7",
            Title = "  The Hobbit ",
            AuthorsText = "J.R.R. Tolkien",
            GenresText = "Fantasy, Classics",
            PageCount = 310,
            Series = "Middle-earth",
            SeriesNumber = 0,
            Status = OwnershipStatus.LentOut,
            LentTo = "Sam",
            Format = BookFormat.Hardcover,
            RatingStars = 4,
            SpineColor = "#2e5e4e",
            Style = SpineStyleChoice.Ornate,
        };
        var book = new Book();
        var item = new LibraryItem();

        model.ApplyTo(book, item);

        Assert.Equal("9780261102217", book.Isbn13);
        Assert.Equal("The Hobbit", book.Title);
        Assert.Equal(new[] { "J.R.R. Tolkien" }, book.Authors.ToArray());
        Assert.Equal(new[] { "Fantasy", "Classics" }, book.Genres.ToArray());
        Assert.Equal("Sam", item.LentTo);
        Assert.Equal(4m, item.Rating);
        Assert.Equal("#2E5E4E", item.Appearance.SpineColor);
        Assert.Equal(SpineStyle.Ornate, item.Appearance.Style);
    }

    [Fact]
    public void ApplyTo_clears_dependent_fields()
    {
        var model = new BookEditModel { Title = "X", Status = OwnershipStatus.Owned, LentTo = "Sam", SeriesNumber = 2, PageCount = 0 };
        var book = new Book();
        var item = new LibraryItem();

        model.ApplyTo(book, item);

        Assert.Null(item.LentTo);
        Assert.Null(book.SeriesNumber);
        Assert.Null(book.PageCount);
        Assert.Null(item.Rating);
        Assert.Null(item.Appearance.Style);
    }

    [Fact]
    public void Spine_color_matching_the_cover_is_not_stored_as_an_override()
    {
        var book = new Book { Title = "Dune", DominantColor = "#D9822B" };
        var item = new LibraryItem { BookId = book.Id };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));

        model.ApplyTo(book, item);
        Assert.Null(item.Appearance.SpineColor);

        model.SpineColor = "#111111";
        model.ApplyTo(book, item);
        Assert.Equal("#111111", item.Appearance.SpineColor);
    }

    [Fact]
    public void Half_star_rating_survives_an_edit_that_does_not_touch_it()
    {
        var book = new Book { Title = "Circe" };
        var item = new LibraryItem { BookId = book.Id, Rating = 3.5m };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));

        model.Title = "Circe (2018)";
        model.ApplyTo(book, item);
        Assert.Equal(3.5m, item.Rating);

        model.RatingStars = 2;
        model.ApplyTo(book, item);
        Assert.Equal(2m, item.Rating);

        model.RatingStars = 0;
        model.ApplyTo(book, item);
        Assert.Null(item.Rating);
    }

    [Fact]
    public void FromEntry_round_trips_without_changes()
    {
        var book = new Book { Title = "Piranesi", Authors = ["Susanna Clarke"], Genres = ["Fantasy"], PageCount = 272 };
        var item = new LibraryItem
        {
            BookId = book.Id,
            Format = BookFormat.Hardcover,
            WantToReread = true,
            Appearance = new SpineAppearance { SpineColor = "#7FB7BE", TextColor = "#E9C46A", FaceOut = true, Style = SpineStyle.Minimal },
        };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));
        var book2 = new Book();
        var item2 = new LibraryItem();

        model.ApplyTo(book2, item2);

        Assert.Equal(book.Title, book2.Title);
        Assert.Equal(book.Authors, book2.Authors);
        Assert.Equal(item.Format, item2.Format);
        Assert.True(item2.WantToReread);
        Assert.True(item2.Appearance.FaceOut);
        Assert.Equal(SpineStyle.Minimal, item2.Appearance.Style);
        Assert.Equal("#7FB7BE", item2.Appearance.SpineColor);
        Assert.Equal("#E9C46A", item2.Appearance.TextColor);
    }

    [Fact]
    public void Text_color_defaults_to_automatic_and_can_be_overridden_and_cleared()
    {
        var model = BookEditModel.ForNew();
        var book = new Book();
        var item = new LibraryItem();
        Assert.Null(model.TextColor);

        model.Title = "X";
        model.TextColor = "#f4ebd9";
        model.ApplyTo(book, item);
        Assert.Equal("#F4EBD9", item.Appearance.TextColor);

        model.TextColor = null;
        model.ApplyTo(book, item);
        Assert.Null(item.Appearance.TextColor);
    }

    [Fact]
    public void A_spine_photo_survives_an_edit_and_can_be_removed()
    {
        const string photo = "data:image/webp;base64,UklGRg==";
        var book = new Book { Title = "Dune" };
        var item = new LibraryItem { BookId = book.Id, Appearance = { SpineImageUrl = photo } };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));

        model.ApplyTo(book, item);
        Assert.Equal(photo, item.Appearance.SpineImageUrl);

        model.SpineImageUrl = null;
        model.ApplyTo(book, item);
        Assert.Null(item.Appearance.SpineImageUrl);
    }

    [Fact]
    public void A_generated_design_is_stored_with_its_accent_and_dropped_for_plain_styles()
    {
        var book = new Book { Title = "Dune" };
        var item = new LibraryItem { BookId = book.Id };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));

        model.ApplyDesign(new SpineDesign(SpineStyle.Penguin, "#d9822b", "#2B3A55", "#1A1A1A"));
        model.ApplyTo(book, item);

        Assert.Equal(SpineStyle.Penguin, item.Appearance.Style);
        Assert.Equal("#D9822B", item.Appearance.SpineColor);
        Assert.Equal("#2B3A55", item.Appearance.AccentColor);
        Assert.Equal(SpineStyleChoice.Penguin, BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>())).Style);

        model.Style = SpineStyleChoice.Classic;
        model.ApplyTo(book, item);
        Assert.Null(item.Appearance.AccentColor);
    }

    [Fact]
    public void ForNew_picks_a_palette_color()
        => Assert.Contains(BookEditModel.ForNew(random: new Random(1)).SpineColor!, BookEditModel.SpinePalette);
}
