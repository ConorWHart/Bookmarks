namespace Bookmarks.Core.Ranking;

/// <summary>
/// Moves one item within a rank-ordered list and works out which rank keys must change.
/// Normally that's just the moved item (a key between its new neighbours). If the neighbours'
/// keys are missing or out of order, the whole list is renumbered once.
/// </summary>
public static class Reorder
{
    /// <param name="keys">Current keys in display order (may contain nulls).</param>
    /// <param name="from">Index of the item to move.</param>
    /// <param name="to">Its index after the move (clamped to the list).</param>
    /// <returns>The writes to make: (index in the original list, new key). Empty if nothing moves.</returns>
    public static IReadOnlyList<(int Index, string Key)> Move(IReadOnlyList<string?> keys, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, keys.Count);

        to = Math.Clamp(to, 0, keys.Count - 1);
        if (to == from)
        {
            return [];
        }

        // Original indexes in their new order.
        var order = Enumerable.Range(0, keys.Count).ToList();
        order.RemoveAt(from);
        order.Insert(to, from);

        var before = to > 0 ? keys[order[to - 1]] : null;
        var after = to < order.Count - 1 ? keys[order[to + 1]] : null;
        var hasBefore = to > 0;
        var hasAfter = to < order.Count - 1;

        if ((!hasBefore || IsUsable(before)) && (!hasAfter || IsUsable(after))
            && (before is null || after is null || RankKey.Compare(before, after) < 0))
        {
            return [(from, RankKey.Between(before, after))];
        }

        // Neighbours can't be trusted: renumber everything in the new order.
        var fresh = RankKey.Sequence(keys.Count);
        var writes = new List<(int Index, string Key)>();
        for (var i = 0; i < order.Count; i++)
        {
            if (!string.Equals(keys[order[i]], fresh[i], StringComparison.Ordinal))
            {
                writes.Add((order[i], fresh[i]));
            }
        }

        return writes;
    }

    /// <summary>
    /// A key for an item inserted into another rank-ordered list (dragged in from another shelf).
    /// </summary>
    /// <param name="keys">The target list's current keys in display order (without the inserted item).</param>
    /// <param name="index">Where it goes (clamped to 0..Count).</param>
    /// <returns>The inserted item's key, plus writes for existing items (index in <paramref name="keys"/>, new key):
    /// normally none; if the neighbours' keys are missing or out of order, the whole list is renumbered once.</returns>
    public static (string Key, IReadOnlyList<(int Index, string Key)> Renumber) Insert(IReadOnlyList<string?> keys, int index)
    {
        ArgumentNullException.ThrowIfNull(keys);
        index = Math.Clamp(index, 0, keys.Count);

        var before = index > 0 ? keys[index - 1] : null;
        var after = index < keys.Count ? keys[index] : null;
        if ((index == 0 || IsUsable(before)) && (index == keys.Count || IsUsable(after))
            && (before is null || after is null || RankKey.Compare(before, after) < 0))
        {
            return (RankKey.Between(before, after), []);
        }

        var fresh = RankKey.Sequence(keys.Count + 1);
        var writes = new List<(int Index, string Key)>();
        for (var i = 0; i < keys.Count; i++)
        {
            var key = fresh[i < index ? i : i + 1];
            if (!string.Equals(keys[i], key, StringComparison.Ordinal))
            {
                writes.Add((i, key));
            }
        }

        return (fresh[index], writes);
    }

    private static bool IsUsable(string? key) => !string.IsNullOrEmpty(key) && !key.EndsWith('0');
}
