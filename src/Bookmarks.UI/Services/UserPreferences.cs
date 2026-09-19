using System.Globalization;
using Bookmarks.Core.Storage;

namespace Bookmarks.UI.Services;

/// <summary>
/// Per-device UI preferences (library size, later display defaults), kept in the local store's
/// meta table under a "pref:" prefix. They don't sync; presets (Phase 6) are the synced equivalent.
/// </summary>
public sealed class UserPreferences(ILocalStore store)
{
    public const string LibraryScale = "library.scale";

    /// <summary>Ids of bookcases collapsed on the Bookcases page.</summary>
    public const string CollapsedBookcases = "bookcases.collapsed";

    private const string Prefix = "pref:";

    public async Task<double?> GetDoubleAsync(string key, CancellationToken ct = default)
    {
        var raw = await store.GetMetaAsync(Prefix + key, ct);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public Task SetDoubleAsync(string key, double value, CancellationToken ct = default)
        => store.SetMetaAsync(Prefix + key, value.ToString("R", CultureInfo.InvariantCulture), ct);

    public async Task<HashSet<Guid>> GetGuidSetAsync(string key, CancellationToken ct = default)
    {
        var raw = await store.GetMetaAsync(Prefix + key, ct);
        var set = new HashSet<Guid>();
        foreach (var part in (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var id))
            {
                set.Add(id);
            }
        }

        return set;
    }

    /// <summary>An empty set removes the key.</summary>
    public Task SetGuidSetAsync(string key, IEnumerable<Guid> values, CancellationToken ct = default)
    {
        var raw = string.Join(",", values.Select(v => v.ToString("D")));
        return store.SetMetaAsync(Prefix + key, raw.Length == 0 ? null : raw, ct);
    }
}
