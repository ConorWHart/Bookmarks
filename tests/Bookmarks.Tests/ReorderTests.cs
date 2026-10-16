using Bookmarks.Core.Ranking;

namespace Bookmarks.Tests;

public class ReorderTests
{
    private static string?[] Apply(IReadOnlyList<string?> keys, IReadOnlyList<(int Index, string Key)> writes)
    {
        var result = keys.ToArray();
        foreach (var (index, key) in writes)
        {
            result[index] = key;
        }

        return result;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Insert_lands_between_its_neighbours_with_one_write(int index)
    {
        var keys = RankKey.Sequence(3).Cast<string?>().ToArray();

        var (key, renumber) = Reorder.Insert(keys, index);

        Assert.Empty(renumber);
        var all = keys.ToList();
        all.Insert(index, key);
        Assert.Equal(all.Order(StringComparer.Ordinal), all);
    }

    [Fact]
    public void Insert_into_an_empty_list()
    {
        var (key, renumber) = Reorder.Insert([], 5);

        Assert.False(string.IsNullOrEmpty(key));
        Assert.Empty(renumber);
    }

    [Fact]
    public void Insert_renumbers_when_neighbours_are_unusable()
    {
        string?[] keys = ["V", null, "V"];

        var (key, renumber) = Reorder.Insert(keys, 1);

        var result = Apply(keys, renumber).ToList();
        result.Insert(1, key);
        Assert.Equal(result.Order(StringComparer.Ordinal), result);
        Assert.Equal(result.Count, result.Distinct().Count());
    }

    /// <summary>Original indexes sorted by their (new) key.</summary>
    private static int[] OrderOf(string?[] keys)
        => Enumerable.Range(0, keys.Length).OrderBy(i => keys[i], StringComparer.Ordinal).ToArray();

    [Theory]
    [InlineData(0, 2, new[] { 1, 2, 0, 3 })]
    [InlineData(3, 0, new[] { 3, 0, 1, 2 })]
    [InlineData(1, 2, new[] { 0, 2, 1, 3 })]
    [InlineData(2, 1, new[] { 0, 2, 1, 3 })]
    [InlineData(0, 3, new[] { 1, 2, 3, 0 })]
    public void Moving_with_valid_keys_rewrites_only_the_moved_item(int from, int to, int[] expected)
    {
        var keys = RankKey.Sequence(4).Cast<string?>().ToArray();

        var writes = Reorder.Move(keys, from, to);

        Assert.Single(writes);
        Assert.Equal(from, writes[0].Index);
        Assert.Equal(expected, OrderOf(Apply(keys, writes)));
    }

    [Fact]
    public void Same_position_is_a_no_op()
        => Assert.Empty(Reorder.Move(RankKey.Sequence(3).Cast<string?>().ToArray(), 1, 1));

    [Fact]
    public void Target_is_clamped()
    {
        var keys = RankKey.Sequence(3).Cast<string?>().ToArray();
        Assert.Equal(new[] { 1, 2, 0 }, OrderOf(Apply(keys, Reorder.Move(keys, 0, 99))));
    }

    [Fact]
    public void Missing_or_duplicate_keys_trigger_a_renumber()
    {
        var keys = new string?[] { null, "V", "V", null };

        var result = Apply(keys, Reorder.Move(keys, 3, 1));

        Assert.All(result, k => Assert.False(string.IsNullOrEmpty(k)));
        Assert.Equal(new[] { 0, 3, 1, 2 }, OrderOf(result));
    }
}
