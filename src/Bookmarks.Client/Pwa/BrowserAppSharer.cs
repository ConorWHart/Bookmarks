using Bookmarks.UI.Services;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Pwa;

/// <summary><see cref="IAppSharer"/> for the PWA: the Web Share API, else the clipboard (<c>js/share.js</c>).</summary>
public sealed class BrowserAppSharer(IJSRuntime js) : IAppSharer, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public bool IsSupported => true;

    public async Task<bool> CanShareNativelyAsync(CancellationToken ct = default)
        => await (await ModuleAsync(ct)).InvokeAsync<bool>("canShare", ct);

    public async Task<ShareOutcome> ShareAsync(string title, string text, string url, CancellationToken ct = default)
    {
        var result = await (await ModuleAsync(ct)).InvokeAsync<string>("share", ct, new object?[] { title, text, url });
        return result switch
        {
            "shared" => ShareOutcome.Shared,
            "cancelled" => ShareOutcome.Cancelled,
            "copied" => ShareOutcome.Copied,
            _ => ShareOutcome.Failed,
        };
    }

    public async Task<bool> CopyAsync(string text, CancellationToken ct = default)
        => await (await ModuleAsync(ct)).InvokeAsync<bool>("copy", ct, new object?[] { text });

    private async ValueTask<IJSObjectReference> ModuleAsync(CancellationToken ct)
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", ct, new object?[] { "./js/share.js" });

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
