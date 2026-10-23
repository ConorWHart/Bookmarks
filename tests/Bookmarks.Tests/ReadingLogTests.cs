using Bookmarks.Core.Library;
using Bookmarks.Shared.Domain;

namespace Bookmarks.Tests;

public class ReadingLogTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    private static LibraryEntry Book(string title, string author = "Test Author", int? pages = 300, decimal? rating = null)
    {
        var entry = TestData.Entry(title, author, pages: pages);
        entry.Item.Rating = rating;
        return entry;
    }

    private static ReadingSession Read(LibraryEntry entry, DateOnly? finished, ReadingOutcome outcome = ReadingOutcome.Finished,
        DateOnly? started = null, decimal? rating = null)
        => new()
        {
            LibraryItemId = entry.Id,
            Outcome = outcome,
            StartedOn = started,
            FinishedOn = finished,
            Rating = rating,
        };

    [Fact]
    public void Current_lists_books_in_progress_with_days_so_far_newest_start_first()
    {
        var dune = Book("Dune");
        var emma = Book("Emma");
        var sessions = new[]
        {
            Read(dune, null, ReadingOutcome.InProgress, started: new DateOnly(2026, 10, 20)),
            Read(emma, null, ReadingOutcome.InProgress),
            Read(emma, new DateOnly(2025, 1, 1)),
        };

        var current = ReadingLog.Current([dune, emma], sessions, Today);

        Assert.Equal(new[] { "Dune", "Emma" }, current.Select(c => c.Entry.Title));
        Assert.Equal(3, current[0].DaysSoFar); // 20th, 21st, 22nd
        Assert.Null(current[1].DaysSoFar);
    }

    [Fact]
    public void Done_is_newest_first_with_undated_last_and_numbers_rereads()
    {
        var dune = Book("Dune", rating: 4m);
        var sessions = new[]
        {
            Read(dune, new DateOnly(2024, 5, 1)),
            Read(dune, null),
            Read(dune, new DateOnly(2026, 3, 1), rating: 5m),
            Read(dune, new DateOnly(2025, 6, 1), ReadingOutcome.Abandoned),
        };

        var done = ReadingLog.Done([dune], sessions);

        Assert.Equal(new[] { "2026-03-01", "2025-06-01", "2024-05-01", "none" }, done.Select(r => r.Session.FinishedOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "none"));
        Assert.Equal(new[] { 3, 0, 2, 1 }, done.Select(r => r.ReadNumber)); // undated counts as the first read
        Assert.Equal(5m, done[0].Rating); // the read's own rating
        Assert.Equal(4m, done[2].Rating); // else the book's
    }

    [Fact]
    public void Deleted_sessions_and_books_no_longer_in_the_library_are_left_out()
    {
        var dune = Book("Dune");
        var gone = Book("Gone");
        var deleted = Read(dune, new DateOnly(2026, 1, 1));
        deleted.IsDeleted = true;

        var done = ReadingLog.Done([dune], [deleted, Read(gone, new DateOnly(2026, 2, 1))]);

        Assert.Empty(done);
    }

    [Fact]
    public void Timeline_groups_by_month_newest_first_and_undated_last()
    {
        var a = Book("A");
        var sessions = new[]
        {
            Read(a, new DateOnly(2026, 3, 5)),
            Read(a, new DateOnly(2026, 3, 20)),
            Read(a, new DateOnly(2025, 12, 1)),
            Read(a, null),
        };

        var months = ReadingLog.Timeline([a], sessions);

        Assert.Equal(new int?[] { 2026, 2025, null }, months.Select(m => m.Year));
        Assert.Equal(new[] { 3, 12, 0 }, months.Select(m => m.Month));
        Assert.Equal(2, months[0].Reads.Count);
        Assert.Equal(new DateOnly(2026, 3, 20), months[0].Reads[0].Session.FinishedOn);
    }

    [Fact]
    public void Years_include_the_current_year()
    {
        var a = Book("A");

        var years = ReadingLog.Years([a], [Read(a, new DateOnly(2023, 1, 1)), Read(a, new DateOnly(2025, 1, 1))], Today);

        Assert.Equal(new[] { 2026, 2025, 2023 }, years);
    }

    [Fact]
    public void Year_stats()
    {
        var dune = Book("Dune", "Frank Herbert", pages: 600, rating: 5m);
        var messiah = Book("Dune Messiah", "Frank Herbert", pages: 250, rating: 4m);
        var emma = Book("Emma", "Jane Austen", pages: null);
        var sessions = new[]
        {
            Read(dune, new DateOnly(2026, 1, 10)),
            Read(dune, new DateOnly(2026, 9, 2)),            // a re-read counts again
            Read(messiah, new DateOnly(2026, 9, 30), rating: 3m),
            Read(emma, new DateOnly(2026, 2, 1)),            // no pages, no rating
            Read(emma, new DateOnly(2026, 3, 1), ReadingOutcome.Abandoned),
            Read(dune, new DateOnly(2025, 12, 31)),          // other year
        };

        var stats = ReadingLog.YearStats([dune, messiah, emma], sessions, 2026);

        Assert.Equal(4, stats.Reads);
        Assert.Equal(3, stats.Books);
        Assert.Equal(1, stats.Stopped);
        Assert.Equal(600 + 600 + 250, stats.PagesRead);
        Assert.Equal(4.3m, stats.AverageRating); // (5 + 5 + 3) / 3 = 4.33
        Assert.Equal("Dune", stats.Longest!.Entry.Title);
        Assert.Equal("Dune Messiah", stats.Shortest!.Entry.Title);
        Assert.Equal("Frank Herbert", stats.TopAuthor);
        Assert.Equal(3, stats.TopAuthorReads);
        Assert.Equal(new[] { 1, 1, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0 }, stats.PerMonth);
        Assert.Equal(new[] { 600, 0, 0, 0, 0, 0, 0, 0, 850, 0, 0, 0 }, stats.PagesPerMonth);
    }

    [Fact]
    public void Logged_pages_count_on_their_days_and_a_finished_book_adds_only_its_unlogged_rest()
    {
        var dune = Book("Dune", pages: 600);
        var emma = Book("Emma", pages: null);
        var finished = Read(dune, new DateOnly(2026, 3, 10));
        finished.PageLog = [new(new DateOnly(2026, 2, 27), 100), new(new DateOnly(2026, 3, 1), 150)];
        var reading = Read(emma, null, ReadingOutcome.InProgress);
        reading.PageLog = [new(new DateOnly(2026, 3, 2), 40), new(new DateOnly(2025, 12, 31), 99)];

        var stats = ReadingLog.YearStats([dune, emma], [finished, reading], 2026);

        Assert.Equal(100 + 150 + 350 + 40, stats.PagesRead);
        Assert.Equal(100, stats.PagesPerMonth[1]);
        Assert.Equal(150 + 350 + 40, stats.PagesPerMonth[2]);
    }

    [Fact]
    public void Pages_per_day_runs_from_new_year_to_today_and_sums_books()
    {
        var a = Book("A");
        var b = Book("B");
        var one = Read(a, null, ReadingOutcome.InProgress);
        one.PageLog = [new(new DateOnly(2026, 1, 2), 30), new(new DateOnly(2026, 10, 22), 12)];
        var two = Read(b, new DateOnly(2026, 1, 5));
        two.PageLog = [new(new DateOnly(2026, 1, 2), 20)];

        var days = ReadingLog.PagesPerDay([a, b], [one, two], 2026, Today);

        Assert.Equal(Today.DayOfYear, days.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), days[0].Date);
        Assert.Equal(50, days[1].Pages);
        Assert.Equal(12, days[^1].Pages);
        Assert.Equal(62, days.Sum(d => d.Pages)); // B's unlogged rest has no day
        Assert.Equal(365, ReadingLog.PagesPerDay([a], [], 2025, Today).Count);
        Assert.Empty(ReadingLog.PagesPerDay([a], [], 2027, Today));
    }

    [Fact]
    public void Current_reads_total_their_logged_pages()
    {
        var a = Book("A");
        var read = Read(a, null, ReadingOutcome.InProgress);
        read.PageLog = [new(new DateOnly(2026, 10, 20), 25), new(new DateOnly(2026, 10, 21), 30)];

        Assert.Equal(55, ReadingLog.Current([a], [read], Today)[0].PagesLogged);
    }

    [Fact]
    public void Adding_pages_merges_a_day_sorts_and_drops_empty_days()
    {
        List<DailyPages> log = [new(new DateOnly(2026, 10, 21), 20)];

        log = ReadingLog.AddPages(log, new DateOnly(2026, 10, 20), 10);
        log = ReadingLog.AddPages(log, new DateOnly(2026, 10, 21), 5);

        Assert.Equal(new DailyPages[] { new(new DateOnly(2026, 10, 20), 10), new(new DateOnly(2026, 10, 21), 25) }, log);
        Assert.Single(ReadingLog.AddPages(log, new DateOnly(2026, 10, 20), -10));
    }

    [Fact]
    public void Setting_a_days_pages_replaces_it_and_null_removes_it()
    {
        List<DailyPages> log = [new(new DateOnly(2026, 10, 21), 20), new(new DateOnly(2026, 10, 1), 5)];

        var set = ReadingLog.SetPages(log, new DateOnly(2026, 10, 21), 50);

        Assert.Equal(new DailyPages[] { new(new DateOnly(2026, 10, 1), 5), new(new DateOnly(2026, 10, 21), 50) }, set);
        Assert.Single(ReadingLog.SetPages(log, new DateOnly(2026, 10, 21), null));
    }

    [Fact]
    public void An_empty_year_has_zeroes_and_no_highlights()
    {
        var stats = ReadingLog.YearStats([Book("A")], [], 2026);

        Assert.Equal(0, stats.Reads);
        Assert.Null(stats.AverageRating);
        Assert.Null(stats.Longest);
        Assert.Null(stats.Shortest);
        Assert.Null(stats.TopAuthor);
        Assert.Equal(12, stats.PerMonth.Count);
        Assert.Equal(12, stats.PagesPerMonth.Count);
    }
}
