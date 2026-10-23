using Bookmarks.Shared.Domain;

namespace Bookmarks.Core.Library;

/// <summary>A book being read now.</summary>
/// <param name="DaysSoFar">Days since it was started (counting today as day 1), or null when the start date isn't known.</param>
public sealed record CurrentRead(LibraryEntry Entry, ReadingSession Session, int? DaysSoFar)
{
    /// <summary>Pages logged so far on this read (its page log).</summary>
    public int PagesLogged => Session.PageLog.Sum(d => d.Pages);
}

/// <summary>A finished or stopped read.</summary>
/// <param name="Rating">The read's own rating, else the book's.</param>
/// <param name="ReadNumber">For a finished read: which time it was (1 = first); 0 for a stopped read.</param>
public sealed record LoggedRead(LibraryEntry Entry, ReadingSession Session, decimal? Rating, int ReadNumber)
{
    public bool IsFinished => Session.Outcome == ReadingOutcome.Finished;
}

/// <summary>The timeline's reads of one month, newest first. <see cref="Year"/> is null for reads with no end date.</summary>
public sealed record ReadingMonth(int? Year, int Month, IReadOnlyList<LoggedRead> Reads);

/// <summary>One year of finished reading (Reading log stats).</summary>
/// <param name="Reads">Finished reads that year (a re-read counts again).</param>
/// <param name="Books">Different books finished.</param>
/// <param name="Stopped">Reads stopped that year.</param>
/// <param name="PagesRead">
/// Pages logged on days in the year (any read), plus, for each read finished that year, the rest of the book's page count
/// not covered by its log (all of it when nothing was logged). Books without a page count add only what was logged.
/// </param>
/// <param name="AverageRating">Of the rated finished reads, to one decimal; null when none is rated.</param>
/// <param name="Longest">Finished book with the most pages (null when no page counts are known).</param>
/// <param name="Shortest">Finished book with the fewest pages, when it differs from <see cref="Longest"/>.</param>
/// <param name="TopAuthor">Author with the most finished reads, when that's at least two.</param>
/// <param name="PerMonth">Finished reads per month, January first (12 values).</param>
/// <param name="PagesPerMonth"><see cref="PagesRead"/> per month, January first (12 values).</param>
public sealed record ReadingYearStats(
    int Year,
    int Reads,
    int Books,
    int Stopped,
    int PagesRead,
    decimal? AverageRating,
    LoggedRead? Longest,
    LoggedRead? Shortest,
    string? TopAuthor,
    int TopAuthorReads,
    IReadOnlyList<int> PerMonth,
    IReadOnlyList<int> PagesPerMonth);

/// <summary>The Reading log page (Phase 7d), from the library's reading sessions. No state of its own.</summary>
public static class ReadingLog
{
    /// <summary>Books in progress, most recently started first (unknown start dates last).</summary>
    public static IReadOnlyList<CurrentRead> Current(
        IEnumerable<LibraryEntry> entries, IEnumerable<ReadingSession> sessions, DateOnly today)
        => Join(entries, sessions)
            .Where(x => x.Session.Outcome == ReadingOutcome.InProgress)
            .OrderByDescending(x => x.Session.StartedOn ?? DateOnly.MinValue)
            .ThenBy(x => x.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new CurrentRead(
                x.Entry,
                x.Session,
                x.Session.StartedOn is { } started && started <= today ? today.DayNumber - started.DayNumber + 1 : null))
            .ToList();

    /// <summary>Finished and stopped reads, newest first; reads with no end date come last.</summary>
    public static IReadOnlyList<LoggedRead> Done(IEnumerable<LibraryEntry> entries, IEnumerable<ReadingSession> sessions)
    {
        var done = Join(entries, sessions)
            .Where(x => x.Session.Outcome is ReadingOutcome.Finished or ReadingOutcome.Abandoned)
            .ToList();

        // Which read of the book each finished read was: oldest first, undated reads counted as the earliest.
        var readNumbers = done
            .Where(x => x.Session.Outcome == ReadingOutcome.Finished)
            .GroupBy(x => x.Entry.Id)
            .SelectMany(g => g
                .OrderBy(x => x.Session.FinishedOn ?? DateOnly.MinValue)
                .ThenBy(x => x.Session.Id)
                .Select((x, i) => (x.Session.Id, Number: i + 1)))
            .ToDictionary(x => x.Id, x => x.Number);

        return done
            .OrderByDescending(x => x.Session.FinishedOn.HasValue)
            .ThenByDescending(x => x.Session.FinishedOn)
            .ThenByDescending(x => x.Session.StartedOn)
            .ThenByDescending(x => x.Session.Id)
            .Select(x => new LoggedRead(
                x.Entry,
                x.Session,
                x.Session.Rating ?? x.Entry.Item.Rating,
                readNumbers.GetValueOrDefault(x.Session.Id)))
            .ToList();
    }

    /// <summary>Done reads grouped by the month they ended, newest month first; undated reads in one last group.</summary>
    public static IReadOnlyList<ReadingMonth> Timeline(IEnumerable<LibraryEntry> entries, IEnumerable<ReadingSession> sessions)
        => Done(entries, sessions)
            .GroupBy(r => r.Session.FinishedOn is { } d ? (Year: (int?)d.Year, d.Month) : (Year: (int?)null, Month: 0))
            .Select(g => new ReadingMonth(g.Key.Year, g.Key.Month, g.ToList()))
            .ToList(); // Done is already newest first, so the groups come out in order.

    /// <summary>Years with a finished or stopped read, plus the current year; newest first.</summary>
    public static IReadOnlyList<int> Years(IEnumerable<LibraryEntry> entries, IEnumerable<ReadingSession> sessions, DateOnly today)
        => Done(entries, sessions)
            .Where(r => r.Session.FinishedOn is not null)
            .Select(r => r.Session.FinishedOn!.Value.Year)
            .Append(today.Year)
            .Distinct()
            .OrderDescending()
            .ToList();

    public static ReadingYearStats YearStats(IEnumerable<LibraryEntry> entries, IEnumerable<ReadingSession> sessions, int year)
    {
        var inYear = Done(entries, sessions).Where(r => r.Session.FinishedOn?.Year == year).ToList();
        var finished = inYear.Where(r => r.IsFinished).ToList();

        var rated = finished.Where(r => r.Rating is not null).Select(r => r.Rating!.Value).ToList();
        decimal? average = rated.Count == 0 ? null : Math.Round(rated.Average(), 1, MidpointRounding.AwayFromZero);

        var withPages = finished
            .Where(r => r.Entry.Book.PageCount is > 0)
            .DistinctBy(r => r.Entry.Book.Id)
            .ToList();
        var longest = withPages.OrderByDescending(r => r.Entry.Book.PageCount).ThenBy(r => r.Entry.Title).FirstOrDefault();
        var shortest = withPages.OrderBy(r => r.Entry.Book.PageCount).ThenBy(r => r.Entry.Title).FirstOrDefault();
        if (shortest is not null && longest is not null && shortest.Entry.Book.Id == longest.Entry.Book.Id)
        {
            shortest = null;
        }

        var top = finished
            .Where(r => !string.IsNullOrWhiteSpace(r.Entry.Author))
            .GroupBy(r => r.Entry.Author.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => (Author: g.Key, Reads: g.Count()))
            .OrderByDescending(a => a.Reads)
            .ThenBy(a => a.Author, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();

        var perMonth = new int[12];
        foreach (var read in finished)
        {
            perMonth[read.Session.FinishedOn!.Value.Month - 1]++;
        }

        var pagesPerMonth = new int[12];
        foreach (var (date, pages) in PagesByDate(entries, sessions))
        {
            if (date.Year == year)
            {
                pagesPerMonth[date.Month - 1] += pages;
            }
        }

        return new ReadingYearStats(
            year,
            finished.Count,
            finished.Select(r => r.Entry.Book.Id).Distinct().Count(),
            inYear.Count - finished.Count,
            pagesPerMonth.Sum(),
            average,
            longest,
            shortest,
            top.Reads >= 2 ? top.Author : null,
            top.Reads >= 2 ? top.Reads : 0,
            perMonth,
            pagesPerMonth);
    }

    /// <summary>
    /// Pages logged each day of <paramref name="year"/>, from 1 January to 31 December, or to today in the current year
    /// (empty for a later year). Only the page logs count: a finished book's unlogged pages have no day to go on.
    /// </summary>
    public static IReadOnlyList<DailyPages> PagesPerDay(
        IEnumerable<LibraryEntry> entries, IEnumerable<ReadingSession> sessions, int year, DateOnly today)
    {
        var first = new DateOnly(year, 1, 1);
        var last = year == today.Year ? today : new DateOnly(year, 12, 31);
        if (last < first)
        {
            return [];
        }

        var totals = new Dictionary<DateOnly, int>();
        foreach (var (_, session) in Join(entries, sessions))
        {
            foreach (var day in session.PageLog)
            {
                if (day.Date >= first && day.Date <= last && day.Pages > 0)
                {
                    totals[day.Date] = totals.GetValueOrDefault(day.Date) + day.Pages;
                }
            }
        }

        var days = new List<DailyPages>(last.DayNumber - first.DayNumber + 1);
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            days.Add(new DailyPages(date, totals.GetValueOrDefault(date)));
        }

        return days;
    }

    /// <summary>
    /// A page log with <paramref name="pages"/> added on <paramref name="date"/>: added to that day's entry if there is one,
    /// sorted by date. A day whose total drops to zero or below is removed.
    /// </summary>
    public static List<DailyPages> AddPages(IEnumerable<DailyPages> log, DateOnly date, int pages)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log
            .Append(new DailyPages(date, pages))
            .GroupBy(d => d.Date)
            .Select(g => new DailyPages(g.Key, g.Sum(d => d.Pages)))
            .Where(d => d.Pages > 0)
            .OrderBy(d => d.Date)
            .ToList();
    }

    /// <summary>
    /// A page log with <paramref name="date"/> set to exactly <paramref name="pages"/> (replacing that day's entry), sorted
    /// by date. Null, zero or less removes the day.
    /// </summary>
    public static List<DailyPages> SetPages(IEnumerable<DailyPages> log, DateOnly date, int? pages)
    {
        ArgumentNullException.ThrowIfNull(log);
        var rest = log.Where(d => d.Date != date);
        return (pages is > 0 ? rest.Append(new DailyPages(date, pages.Value)) : rest)
            .OrderBy(d => d.Date)
            .ToList();
    }

    /// <summary>Every page read with a date: logged days, and the unlogged rest of each finished book on its end date.</summary>
    private static IEnumerable<(DateOnly Date, int Pages)> PagesByDate(
        IEnumerable<LibraryEntry> entries, IEnumerable<ReadingSession> sessions)
    {
        foreach (var (entry, session) in Join(entries, sessions))
        {
            var logged = 0;
            foreach (var day in session.PageLog)
            {
                if (day.Pages > 0)
                {
                    logged += day.Pages;
                    yield return (day.Date, day.Pages);
                }
            }

            if (session.Outcome == ReadingOutcome.Finished
                && session.FinishedOn is { } finishedOn
                && entry.Book.PageCount is { } pageCount
                && pageCount > logged)
            {
                yield return (finishedOn, pageCount - logged);
            }
        }
    }

    /// <summary>Live sessions of books still in the library (the entries are the live copies).</summary>
    private static IEnumerable<(LibraryEntry Entry, ReadingSession Session)> Join(
        IEnumerable<LibraryEntry> entries, IEnumerable<ReadingSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(sessions);

        var byItem = entries.ToDictionary(e => e.Id);
        foreach (var session in sessions)
        {
            if (!session.IsDeleted && byItem.TryGetValue(session.LibraryItemId, out var entry))
            {
                yield return (entry, session);
            }
        }
    }
}
