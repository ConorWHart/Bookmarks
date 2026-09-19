using Bookmarks.Core.Ranking;

namespace Bookmarks.Tests;

public class RankKeyTests
{
    [Fact]
    public void Between_null_and_null_returns_a_key()
        => Assert.False(string.IsNullOrEmpty(RankKey.Between(null, null)));

    [Fact]
    public void Between_returns_key_strictly_between_neighbours()
    {
        var a = RankKey.Between(null, null);
        var c = RankKey.Between(a, null);
        var b = RankKey.Between(a, c);

        Assert.True(RankKey.Compare(a, b) < 0);
        Assert.True(RankKey.Compare(b, c) < 0);
    }

    [Fact]
    public void Repeated_prepend_and_append_stay_ordered()
    {
        string? first = null;
        string? last = null;
        for (var i = 0; i < 500; i++)
        {
            var newFirst = RankKey.Between(null, first);
            Assert.True(first is null || RankKey.Compare(newFirst, first) < 0);
            first = newFirst;

            var newLast = RankKey.Between(last, null);
            Assert.True(last is null || RankKey.Compare(last, newLast) < 0);
            last = newLast;
        }
    }

    [Fact]
    public void Random_inserts_keep_list_sorted_and_unique()
    {
        var rng = new Random(42);
        var keys = new List<string>();

        for (var i = 0; i < 2_000; i++)
        {
            var at = rng.Next(keys.Count + 1);
            var before = at > 0 ? keys[at - 1] : null;
            var after = at < keys.Count ? keys[at] : null;
            keys.Insert(at, RankKey.Between(before, after));
        }

        var sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(keys, k => k.EndsWith('0'));
    }

    [Fact]
    public void Sequence_is_ascending()
    {
        var keys = RankKey.Sequence(100);
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys);
    }

    [Fact]
    public void Between_rejects_out_of_order_bounds()
        => Assert.Throws<ArgumentException>(() => RankKey.Between("b", "a"));
}
