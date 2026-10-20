using Bookmarks.UI.Services;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Pwa;

/// <summary><see cref="IAppStatusService"/> for the PWA, via <c>wwwroot/js/pwa.js</c>.</summary>
public sealed class BrowserAppStatusService(IJSRuntime js) : IAppStatusService, IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<BrowserAppStatusService>? _self;

    public bool IsOnline { get; private set; } = true;

    public bool UpdateAvailable { get; private set; }

    public event Action? Changed;

    public async Task StartAsync()
    {
        if (_module is not null)
        {
            return;
        }

        _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/pwa.js");
        _self = DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("start", _self);
    }

    public async Task ApplyUpdateAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("applyUpdate");
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        if (_module is null)
        {
            return UpdateCheckResult.Unsupported;
        }

        var result = await _module.InvokeAsync<string>("checkNow");
        return result switch
        {
            "ready" => UpdateCheckResult.Ready,
            "downloading" => UpdateCheckResult.Downloading,
            "current" => UpdateCheckResult.UpToDate,
            "failed" => UpdateCheckResult.Failed,
            _ => UpdateCheckResult.Unsupported,
        };
    }

    [JSInvokable]
    public void OnOnlineChanged(bool online)
    {
        if (IsOnline != online)
        {
            IsOnline = online;
            Changed?.Invoke();
        }
    }

    [JSInvokable]
    public void OnUpdateAvailable()
    {
        UpdateAvailable = true;
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("stop");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Page is going away.
            }
        }

        _self?.Dispose();
    }
}
