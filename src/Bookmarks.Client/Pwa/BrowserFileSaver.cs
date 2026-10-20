using Bookmarks.UI.Services;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Pwa;

/// <summary><see cref="IFileSaver"/> for the PWA: a browser download (<c>js/files.js</c>).</summary>
public sealed class BrowserFileSaver(IJSRuntime js) : IFileSaver, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public bool IsSupported => true;

    public async Task SaveTextAsync(string fileName, string contentType, string text, CancellationToken ct = default)
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", ct, new object?[] { "./js/files.js" });
        await _module.InvokeVoidAsync("saveText", ct, new object?[] { fileName, contentType, text });
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is going away.
            }
        }
    }
}
