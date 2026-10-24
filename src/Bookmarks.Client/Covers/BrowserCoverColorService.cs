using Bookmarks.UI.Services;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Covers;

/// <summary><see cref="ICoverColorService"/> for the PWA, via <c>wwwroot/js/covers.js</c> (canvas sampling).</summary>
public sealed class BrowserCoverColorService(IJSRuntime js) : ICoverColorService, IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    public async Task<string?> DominantColorAsync(string imageUrl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        try
        {
            _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/covers.js").AsTask();
            var module = await _module;
            return await module.InvokeAsync<string?>("dominantColor", ct, new object?[] { imageUrl });
        }
        catch (JSException)
        {
            _module = null; // let the next call retry the import
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> PaletteAsync(string imageUrl, int max = 4, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        try
        {
            _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/covers.js").AsTask();
            var module = await _module;
            return await module.InvokeAsync<string[]?>("palette", ct, new object?[] { imageUrl, max }) ?? [];
        }
        catch (JSException)
        {
            _module = null;
            return [];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is { IsCompletedSuccessfully: true })
        {
            try
            {
                await (await _module).DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Page is going away.
            }
        }
    }
}
