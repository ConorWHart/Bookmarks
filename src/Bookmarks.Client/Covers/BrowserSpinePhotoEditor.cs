using System.Text.Json;
using Bookmarks.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Covers;

/// <summary><see cref="ISpinePhotoEditor"/> for the PWA, via <c>wwwroot/js/spinePhoto.js</c> (Cropper.js, canvas, WebP).</summary>
public sealed class BrowserSpinePhotoEditor(IJSRuntime js) : ISpinePhotoEditor, IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;

    private Task<IJSObjectReference> Module()
        => _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/spinePhoto.js").AsTask();

    public async Task<string?> LoadAsync(ElementReference fileInput, ElementReference image, double aspect)
    {
        try
        {
            return await (await Module()).InvokeAsync<string?>("load", fileInput, image, aspect);
        }
        catch (JSException)
        {
            _module = null; // let the next call retry the import (offline the first time, say)
            return "nocropper";
        }
    }

    public async Task<string?> LoadUrlAsync(string url, ElementReference image, double aspect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        try
        {
            return await (await Module()).InvokeAsync<string?>("loadUrl", url, image, aspect);
        }
        catch (JSException)
        {
            _module = null;
            return "nocropper";
        }
    }

    public async Task RotateAsync(int degrees)
    {
        try
        {
            await (await Module()).InvokeVoidAsync("rotate", degrees);
        }
        catch (JSException)
        {
            // Nothing loaded.
        }
    }

    public async Task SetAspectAsync(double aspect)
    {
        try
        {
            await (await Module()).InvokeVoidAsync("setAspect", aspect);
        }
        catch (JSException)
        {
            // Nothing loaded.
        }
    }

    public async Task<SpinePhoto?> ExportAsync()
    {
        try
        {
            // A JsonElement rather than a record: nothing for the trimmer to remove in published builds.
            var result = await (await Module()).InvokeAsync<JsonElement>("exportPhoto", SpinePhoto.MaxLongEdge, SpinePhoto.MaxChars);
            if (result.ValueKind != JsonValueKind.Object
                || result.GetProperty("dataUrl").GetString() is not { Length: > 0 } url)
            {
                return null;
            }

            var color = result.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            return new SpinePhoto(url, color, result.GetProperty("width").GetInt32(), result.GetProperty("height").GetInt32());
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task CloseAsync()
    {
        if (_module is not { IsCompletedSuccessfully: true })
        {
            return;
        }

        try
        {
            await (await _module).InvokeVoidAsync("close");
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            // Page is going away.
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
