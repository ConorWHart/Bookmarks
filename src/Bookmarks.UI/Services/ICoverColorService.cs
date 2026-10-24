namespace Bookmarks.UI.Services;

/// <summary>
/// Extracts a cover's dominant color for the spine. The PWA does it on a canvas (<c>js/covers.js</c>);
/// hosts without one get <see cref="NullCoverColorService"/> and books keep their chosen spine color.
/// </summary>
public interface ICoverColorService
{
    /// <summary>"#RRGGBB", or null when the image can't be loaded or read (offline, no CORS, placeholder).</summary>
    Task<string?> DominantColorAsync(string imageUrl, CancellationToken ct = default);

    /// <summary>
    /// Up to <paramref name="max"/> distinct colours from the image, most prominent first (Phase 7e spine designs);
    /// empty when it can't be loaded or read.
    /// </summary>
    Task<IReadOnlyList<string>> PaletteAsync(string imageUrl, int max = 4, CancellationToken ct = default);
}

public sealed class NullCoverColorService : ICoverColorService
{
    public Task<string?> DominantColorAsync(string imageUrl, CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> PaletteAsync(string imageUrl, int max = 4, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
