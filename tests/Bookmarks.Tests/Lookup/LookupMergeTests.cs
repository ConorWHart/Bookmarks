using Bookmarks.Core.Library;
using Bookmarks.Core.Lookup;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests.Lookup;

public class LookupMergeTests
{
    private static readonly BookLookupResult HailMary = new()
    {
        Isbn13 = "9780593135204",
        Title = "Project Hail Mary",
        Authors = ["Andy Weir"],
        Publisher = "Ballantine Books",
        PublishedYear = 2021,
        PageCount = 496,
        Genres = ["Science Fiction"],
        CoverUrl = "https://covers.openlibrary.org/b/id/12455001-M.jpg",
        HeightMm = 242,
        WidthMm = 162,
        ThicknessMm = 39,
        Format = BookFormat.Hardcover,
        Source = BookSource.OpenLibrary,
    };

    [Fact]
    public void New_book_takes_everything()
    {
        var model = BookEditModel.ForNew();

        var changed = LookupMerge.Apply(model, HailMary, LookupApplyMode.FillEmpty, isNewBook: true);

        Assert.Equal("Project Hail Mary", model.Title);
        Assert.Equal("Andy Weir", model.AuthorsText);
        Assert.Equal("Science Fiction", model.GenresText);
        Assert.Equal(496, model.PageCount);
        Assert.Equal(HailMary.CoverUrl, model.CoverUrl);
        Assert.Equal(242, model.HeightMm);
        Assert.Equal(BookFormat.Hardcover, model.Format);
        Assert.Equal(BookSource.OpenLibrary, model.Source);
        Assert.Contains("Title", changed);
        Assert.Contains("Format", changed);
    }

    [Fact]
    public void Fill_empty_keeps_what_the_user_entered()
    {
        var model = new BookEditModel { Title = "PHM", AuthorsText = "Weir", PageCount = 480, Format = BookFormat.Paperback };

        var changed = LookupMerge.Apply(model, HailMary, LookupApplyMode.FillEmpty, isNewBook: false);

        Assert.Equal("PHM", model.Title);
        Assert.Equal("Weir", model.AuthorsText);
        Assert.Equal(480, model.PageCount);
        Assert.Equal(BookFormat.Paperback, model.Format); // existing copy: binding untouched
        Assert.Equal("Ballantine Books", model.Publisher);
        Assert.Equal(2021, model.PublishedYear);
        Assert.DoesNotContain("Title", changed);
        Assert.Contains("Publisher", changed);
    }

    [Fact]
    public void Replace_overwrites_but_never_blanks()
    {
        var model = new BookEditModel { Title = "PHM", Subtitle = "My subtitle", AuthorsText = "Weir", PageCount = 480 };

        LookupMerge.Apply(model, HailMary, LookupApplyMode.ReplaceAll, isNewBook: false);

        Assert.Equal("Project Hail Mary", model.Title);
        Assert.Equal("Andy Weir", model.AuthorsText);
        Assert.Equal(496, model.PageCount);
        Assert.Equal("My subtitle", model.Subtitle); // the result had none
    }

    [Fact]
    public void Series_and_number_move_together()
    {
        var model = new BookEditModel { Title = "The Way of Kings" };
        var result = new BookLookupResult { Title = "The Way of Kings", Series = "The Stormlight Archive", SeriesNumber = 1 };

        LookupMerge.Apply(model, result, LookupApplyMode.FillEmpty, isNewBook: false);

        Assert.Equal("The Stormlight Archive", model.Series);
        Assert.Equal(1m, model.SeriesNumber);
    }

    [Fact]
    public void A_new_cover_clears_the_old_cover_color()
    {
        var model = new BookEditModel { Title = "X", CoverUrl = "https://covers.openlibrary.org/b/id/1-M.jpg", DominantColor = "#112233" };

        LookupMerge.Apply(model, HailMary, LookupApplyMode.ReplaceAll, isNewBook: false);

        Assert.Equal(HailMary.CoverUrl, model.CoverUrl);
        Assert.Null(model.DominantColor);
    }

    [Fact]
    public void Same_cover_keeps_its_color()
    {
        var model = new BookEditModel { Title = "X", CoverUrl = HailMary.CoverUrl, DominantColor = "#112233" };

        LookupMerge.Apply(model, HailMary, LookupApplyMode.ReplaceAll, isNewBook: false);

        Assert.Equal("#112233", model.DominantColor);
    }

    [Fact]
    public void Nothing_new_changes_nothing()
    {
        var model = BookEditModel.ForNew();
        LookupMerge.Apply(model, HailMary, LookupApplyMode.FillEmpty, isNewBook: true);

        var changed = LookupMerge.Apply(model, HailMary, LookupApplyMode.FillEmpty, isNewBook: true);

        Assert.Empty(changed);
        Assert.Equal("Nothing new to fill in", LookupMerge.Describe(changed));
    }

    [Fact]
    public void Describe_summarizes()
    {
        Assert.Equal("Updated title", LookupMerge.Describe(["Title"]));
        Assert.Equal("Updated title and authors", LookupMerge.Describe(["Title", "Authors"]));
        Assert.Equal("Updated title, authors and 3 more", LookupMerge.Describe(["Title", "Authors", "Pages", "Year", "Cover"]));
    }
}

public class BookEditModelCoverTests
{
    [Fact]
    public void Cover_color_becomes_the_spine_color_of_a_new_book()
    {
        var model = BookEditModel.ForNew();
        model.Title = "X";

        model.UseCoverColor("#a1b2c3");

        Assert.Equal("#A1B2C3", model.SpineColor);
        var book = new Book();
        var item = new LibraryItem();
        model.ApplyTo(book, item);
        Assert.Equal("#A1B2C3", book.DominantColor);
        Assert.Null(item.Appearance.SpineColor); // follows the cover, no override stored
    }

    [Fact]
    public void A_chosen_spine_color_is_kept()
    {
        var model = BookEditModel.ForNew();
        model.Title = "X";
        model.SpineColor = "#111111"; // picked in the editor

        model.UseCoverColor("#A1B2C3");

        Assert.Equal("#111111", model.SpineColor);
        var book = new Book();
        var item = new LibraryItem();
        model.ApplyTo(book, item);
        Assert.Equal("#111111", item.Appearance.SpineColor);
    }

    [Fact]
    public void Existing_override_is_kept_when_a_cover_arrives()
    {
        var book = new Book { Title = "X" };
        var item = new LibraryItem { BookId = book.Id, Appearance = new SpineAppearance { SpineColor = "#8C2F1B" } };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));

        model.UseCoverColor("#A1B2C3");

        Assert.Equal("#8C2F1B", model.SpineColor);
    }

    [Fact]
    public void Book_without_override_follows_its_new_cover()
    {
        var book = new Book { Title = "X", DominantColor = "#2E5E4E" };
        var item = new LibraryItem { BookId = book.Id };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));

        model.UseCoverColor("#A1B2C3");
        model.ApplyTo(book, item);

        Assert.Equal("#A1B2C3", book.DominantColor);
        Assert.Null(item.Appearance.SpineColor);
    }

    [Fact]
    public void Removing_the_cover_keeps_the_spine_color()
    {
        var book = new Book { Title = "X", CoverUrl = "https://covers.openlibrary.org/b/id/1-M.jpg", DominantColor = "#A1B2C3" };
        var item = new LibraryItem { BookId = book.Id };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));

        model.RemoveCover();
        model.ApplyTo(book, item);

        Assert.Null(book.CoverUrl);
        Assert.Null(book.DominantColor);
        Assert.Equal("#A1B2C3", item.Appearance.SpineColor);
    }

    [Fact]
    public void Cover_details_round_trip()
    {
        var book = new Book
        {
            Title = "X",
            CoverUrl = "https://covers.openlibrary.org/b/id/1-M.jpg",
            DominantColor = "#A1B2C3",
            HeightMm = 240,
            WidthMm = 160,
            ThicknessMm = 30,
            Source = BookSource.OpenLibrary,
        };
        var item = new LibraryItem { BookId = book.Id };
        var model = BookEditModel.FromEntry(LibraryEntry.Create(item, book, Array.Empty<ReadingSession>()));
        var book2 = new Book();

        model.ApplyTo(book2, new LibraryItem());

        Assert.Equal(book.CoverUrl, book2.CoverUrl);
        Assert.Equal(book.DominantColor, book2.DominantColor);
        Assert.Equal(240, book2.HeightMm);
        Assert.Equal(160, book2.WidthMm);
        Assert.Equal(30, book2.ThicknessMm);
        Assert.Equal(BookSource.OpenLibrary, book2.Source);
    }
}
