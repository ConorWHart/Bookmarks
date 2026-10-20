namespace Bookmarks.UI.Services;

/// <summary>Versions for Settings → About.</summary>
public interface IServerInfo
{
    /// <summary>The API's version, or null when it can't be reached (offline, or no API on this host).</summary>
    Task<string?> GetApiVersionAsync(CancellationToken ct = default);
}

public sealed class NullServerInfo : IServerInfo
{
    public Task<string?> GetApiVersionAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
}
