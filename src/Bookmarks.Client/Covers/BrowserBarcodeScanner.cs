using Bookmarks.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Covers;

/// <summary><see cref="IBarcodeScanner"/> for the PWA, via <c>wwwroot/js/scanner.js</c> (BarcodeDetector or ZXing).</summary>
public sealed class BrowserBarcodeScanner(IJSRuntime js) : IBarcodeScanner, IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;
    private DotNetObjectReference<BrowserBarcodeScanner>? _self;
    private IBarcodeScanHandler? _handler;
    private bool? _supported;

    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            _supported ??= await (await ModuleAsync()).InvokeAsync<bool>("isSupported");
        }
        catch (JSException)
        {
            _supported = false;
        }

        return _supported.Value;
    }

    public async Task<string?> StartAsync(ElementReference video, IBarcodeScanHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
        _self ??= DotNetObjectReference.Create(this);
        try
        {
            return await (await ModuleAsync()).InvokeAsync<string?>("start", video, _self);
        }
        catch (JSException)
        {
            return "unknown";
        }
    }

    public async Task StopAsync()
    {
        _handler = null;
        if (_module is { IsCompletedSuccessfully: true })
        {
            try
            {
                await (await _module).InvokeVoidAsync("stop");
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException)
            {
                // Page closing or camera already gone.
            }
        }
    }

    public async Task<bool> TorchAvailableAsync()
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<bool>("torchAvailable");
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async Task<bool> SetTorchAsync(bool on)
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<bool>("setTorch", on);
        }
        catch (JSException)
        {
            return false;
        }
    }

    // Exceptions don't survive the JS round trip intact, so handlers' failures are logged here.
    [JSInvokable]
    public async Task OnDetected(string code)
    {
        try
        {
            if (_handler is { } handler)
            {
                await handler.OnDetectedAsync(code);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Scanner: {ex}");
        }
    }

    [JSInvokable]
    public async Task OnNotIsbn()
    {
        try
        {
            if (_handler is { } handler)
            {
                await handler.OnNotIsbnAsync();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Scanner: {ex}");
        }
    }

    private Task<IJSObjectReference> ModuleAsync()
    {
        if (_module is null || _module.IsFaulted)
        {
            _module = js.InvokeAsync<IJSObjectReference>("import", "./js/scanner.js").AsTask();
        }

        return _module;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_module is { IsCompletedSuccessfully: true })
        {
            try
            {
                await (await _module).DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _self?.Dispose();
    }
}
