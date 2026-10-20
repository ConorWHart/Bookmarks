namespace Bookmarks.UI.Services;

/// <summary>Hands the user a file to keep (backups and exports). The PWA downloads it through the browser.</summary>
public interface IFileSaver
{
    bool IsSupported { get; }

    /// <summary>Offers <paramref name="text"/> as a file (UTF-8).</summary>
    Task SaveTextAsync(string fileName, string contentType, string text, CancellationToken ct = default);
}

public sealed class NullFileSaver : IFileSaver
{
    public bool IsSupported => false;

    public Task SaveTextAsync(string fileName, string contentType, string text, CancellationToken ct = default)
        => throw new NotSupportedException("Saving files isn't available on this host.");
}
