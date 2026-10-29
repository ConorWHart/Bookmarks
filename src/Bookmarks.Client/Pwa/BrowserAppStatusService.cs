using System.Text.Json;
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

    public bool UpdateIsFresh { get; private set; }

    // Until pwa.js reports, assume installed so the install card doesn't flash up.
    public bool IsInstalled { get; private set; } = true;

    public bool CanPromptInstall { get; private set; }

    public bool JustInstalled { get; private set; }

    public InstallPlatform Platform { get; private set; }

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
        await RefreshInstallStateAsync();
    }

    public async Task<bool> PromptInstallAsync()
    {
        if (_module is null)
        {
            return false;
        }

        var outcome = await _module.InvokeAsync<string>("promptInstall");
        await RefreshInstallStateAsync();
        return outcome == "accepted";
    }

    /// <summary>The install prompt became available, or the app was installed from this tab.</summary>
    [JSInvokable]
    public Task OnInstallChanged() => RefreshInstallStateAsync();

    private async Task RefreshInstallStateAsync()
    {
        if (_module is null)
        {
            return;
        }

        // Read as JsonElement, not a record, so trimming in published builds can't break it.
        var info = await _module.InvokeAsync<JsonElement>("installInfo");
        IsInstalled = info.GetProperty("installed").GetBoolean();
        CanPromptInstall = info.GetProperty("canPrompt").GetBoolean();
        JustInstalled = info.GetProperty("installedNow").GetBoolean();
        Platform = info.GetProperty("platform").GetString() switch
        {
            "android-chrome" => InstallPlatform.AndroidChrome,
            "android-samsung" => InstallPlatform.AndroidSamsung,
            "android-firefox" => InstallPlatform.AndroidFirefox,
            "android-other" => InstallPlatform.AndroidOther,
            "ios-safari" => InstallPlatform.IosSafari,
            "ios-other" => InstallPlatform.IosOther,
            "desktop-chromium" => InstallPlatform.DesktopChromium,
            "desktop-firefox" => InstallPlatform.DesktopFirefox,
            "desktop-safari" => InstallPlatform.DesktopSafari,
            _ => InstallPlatform.Unknown,
        };
        Changed?.Invoke();
    }

    public async Task ApplyUpdateAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("applyUpdate");
        }
    }

    public async Task ReloadAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("reloadApp");
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

    /// <param name="fresh">Found just as the app started or came back to the screen (see <see cref="UpdateIsFresh"/>).</param>
    [JSInvokable]
    public void OnUpdateAvailable(bool fresh)
    {
        UpdateAvailable = true;
        UpdateIsFresh = fresh;
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
