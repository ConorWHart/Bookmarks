using Bookmarks.Core.Lookup;

namespace Bookmarks.Tests.Lookup;

public class StackScanTests
{
    private const string Hobbit = "9780547928227";
    private const string Mistborn = "9780765326355";

    private static BookLookupResult Found(string title) => new() { Title = title };

    [Fact]
    public void The_same_isbn_is_only_added_once_whatever_its_form()
    {
        var stack = new StackScan(_ => false);

        Assert.Equal(StackAddOutcome.Added, stack.Add(Hobbit, out _));
        Assert.Equal(StackAddOutcome.AlreadyScanned, stack.Add("0-547-92822-x", out var again));
        Assert.Equal(StackAddOutcome.Invalid, stack.Add("9780547928228", out _));

        Assert.Single(stack.Items);
        Assert.Equal(Hobbit, again!.Isbn13);
    }

    [Fact]
    public void Books_already_in_the_library_are_flagged_and_left_out_by_default()
    {
        var stack = new StackScan(isbn => isbn == Hobbit);
        stack.Add(Hobbit, out var owned);
        stack.Add(Mistborn, out var fresh);

        StackScan.Complete(stack.StartNextLookup()!, Found("The Hobbit"));
        StackScan.Complete(stack.StartNextLookup()!, Found("Mistborn"));

        Assert.True(owned!.AlreadyInLibrary);
        Assert.False(owned.Include);
        Assert.True(fresh!.Include);
        Assert.Equal(new[] { Mistborn }, stack.ToAdd.Select(i => i.Isbn13));

        owned.Include = true; // a second copy, on purpose
        Assert.Equal(new[] { Hobbit, Mistborn }, stack.ToAdd.Select(i => i.Isbn13));
    }

    [Fact]
    public void Lookups_run_in_scan_order_and_missing_books_are_kept_for_later()
    {
        var stack = new StackScan(_ => false);
        stack.Add(Hobbit, out _);
        stack.Add(Mistborn, out _);

        var first = stack.StartNextLookup()!;
        Assert.Equal(Hobbit, first.Isbn13);
        Assert.True(stack.IsLookingUp);
        StackScan.Complete(first, null);

        var second = stack.StartNextLookup()!;
        StackScan.Fail(second);

        Assert.Null(stack.StartNextLookup());
        Assert.False(stack.IsLookingUp);
        Assert.Equal(0, stack.FoundCount);
        Assert.Equal(new[] { Hobbit, Mistborn }, stack.Missing.Select(i => i.Isbn13));
        Assert.Empty(stack.ToAdd);
    }

    [Fact]
    public void Failed_lookups_can_be_retried()
    {
        var stack = new StackScan(_ => false);
        stack.Add(Hobbit, out _);
        StackScan.Fail(stack.StartNextLookup()!);

        stack.RetryFailed();
        var again = stack.StartNextLookup();

        Assert.NotNull(again);
        StackScan.Complete(again, Found("The Hobbit"));
        Assert.Equal(1, stack.FoundCount);
        Assert.Empty(stack.Missing);
    }
}
