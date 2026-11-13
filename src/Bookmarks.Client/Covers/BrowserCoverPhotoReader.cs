using System.Text.Json;
using Bookmarks.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Covers;

/// <summary><see cref="ICoverPhotoReader"/> for the PWA, via <c>wwwroot/js/coverPhoto.js</c> (Clipboard API, canvas, WebP).</summary>
public sealed class BrowserCoverPhotoReader(IJSRuntime js) : ICoverPhotoReader, IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    private Task<IJSObjectReference> Module()
        => _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/coverPhoto.js").AsTask();

    public async Task<PastedCover> ReadClipboardAsync()
    {
        try
        {
            var result = await (await Module()).InvokeAsync<JsonElement>("readClipboard", PastedCover.MaxLongEdge, PastedCover.MaxChars);
            return Parse(result);
        }
        catch (JSException)
        {
            _module = null; // let the next tap retry the import
            return PastedCover.Failed("unreadable");
        }
    }

    public async Task<IAsyncDisposable> ListenForPasteAsync(ElementReference area, Func<PastedCover, Task> onPasted)
    {
        ArgumentNullException.ThrowIfNull(onPasted);
        var target = DotNetObjectReference.Create(new PasteTarget(onPasted));
        try
        {
            var handle = await (await Module()).InvokeAsync<IJSObjectReference>(
                "listen", area, target, PastedCover.MaxLongEdge, PastedCover.MaxChars);
            return new Listener(handle, target);
        }
        catch (JSException)
        {
            _module = null;
            target.Dispose();
            return new Listener(null, null);
        }
    }

    // A JsonElement rather than a record: nothing for the trimmer to remove in published builds.
    private static PastedCover Parse(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
        {
            return PastedCover.Failed("unreadable");
        }

        if (result.TryGetProperty("dataUrl", out var url) && url.GetString() is { Length: > 0 } dataUrl)
        {
            return new PastedCover(dataUrl, null);
        }

        return PastedCover.Failed(result.TryGetProperty("error", out var e) && e.GetString() is { } error ? error : "unreadable");
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

    private sealed class PasteTarget(Func<PastedCover, Task> onPasted)
    {
        [JSInvokable]
        public Task OnPasted(JsonElement result) => onPasted(Parse(result));
    }

    private sealed class Listener(IJSObjectReference? handle, DotNetObjectReference<PasteTarget>? target) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (handle is not null)
            {
                try
                {
                    await handle.InvokeVoidAsync("dispose");
                    await handle.DisposeAsync();
                }
                catch (Exception ex) when (ex is JSException or JSDisconnectedException)
                {
                    // Page is going away.
                }
            }

            target?.Dispose();
        }
    }
}
