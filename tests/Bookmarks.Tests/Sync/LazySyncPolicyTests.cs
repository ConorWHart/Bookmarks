using Bookmarks.Core.Sync;

namespace Bookmarks.Tests.Sync;

public class LazySyncPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static bool Due(TimeSpan? ago, int pending)
        => LazySyncPolicy.IsDue(ago is { } a ? Now - a : null, pending, Now);

    [Fact]
    public void Never_synced_is_due_straight_away()
    {
        Assert.True(Due(null, 0));
        Assert.True(Due(null, 3));
    }

    [Fact]
    public void Recently_synced_waits_with_a_few_changes()
    {
        Assert.False(Due(TimeSpan.FromHours(2), 0));
        Assert.False(Due(TimeSpan.FromHours(2), 3));
        Assert.False(Due(TimeSpan.FromHours(23), LazySyncPolicy.PushBatch - 1));
    }

    [Fact]
    public void A_batch_of_changes_is_due_early()
    {
        Assert.True(Due(TimeSpan.FromMinutes(5), LazySyncPolicy.PushBatch));
        Assert.True(Due(TimeSpan.FromMinutes(5), LazySyncPolicy.PushBatch + 10));
    }

    [Fact]
    public void Changes_are_due_after_a_day()
    {
        Assert.True(Due(TimeSpan.FromHours(24), 1));
        Assert.True(Due(TimeSpan.FromDays(3), 1));
        Assert.False(Due(TimeSpan.FromDays(3), 0));
    }

    [Fact]
    public void Everyone_catches_up_after_a_week()
    {
        Assert.False(Due(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1), 0));
        Assert.True(Due(TimeSpan.FromDays(7), 0));
        Assert.True(Due(TimeSpan.FromDays(30), 0));
    }

    [Fact]
    public void A_last_sync_in_the_future_is_due()
    {
        Assert.True(LazySyncPolicy.IsDue(Now.AddHours(5), 0, Now));
    }
}
