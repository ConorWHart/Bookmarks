namespace Bookmarks.UI.Services;

/// <summary>
/// Extracts a cover's dominant color for the spine. The PWA does it on a canvas (<c>js/covers.js</c>);
/// hosts without one get <see cref="NullCoverColorService"/> and books keep their chosen spine color.
/// </summary>
public interface ICoverColorService
{
    /// <summary>"#RRGGBB", or null when the image can't be loaded or read (offline, no CORS, placeholder).</summary>
    Task<string?> DominantColorAsync(string imageUrl, CancellationToken ct = default);
}

public sealed class NullCoverColorService : ICoverColorService
{
    public Task<string?> DominantColorAsync(string imageUrl, CancellationToken ct = default) => Task.FromResult<string?>(null);
}
