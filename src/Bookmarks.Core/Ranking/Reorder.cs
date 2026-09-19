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

    private static bool IsUsable(string? key) => !string.IsNullOrEmpty(key) && !key.EndsWith('0');
}
