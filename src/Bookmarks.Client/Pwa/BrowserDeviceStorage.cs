using System.Text.Json;
using Bookmarks.Client.Storage;
using Bookmarks.UI.Services;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Pwa;

/// <summary>Settings → This device for the PWA: <c>navigator.storage</c> and the service worker's cover cache (<c>js/device.js</c>).</summary>
public sealed class BrowserDeviceStorage(IJSRuntime js, IndexedDbLocalStore store) : IDeviceStorage, IAsyncDisposable
{
    private const string ModulePath = "./js/device.js";

    private IJSObjectReference? _module;

    public bool IsSupported => true;

    public async Task<DeviceStorageInfo> GetInfoAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);

        // [usage, quota, persisted, canRequestPersistence, coverCount, coverBytes]; numbers can be null.
        var info = await module.InvokeAsync<JsonElement[]>("storageInfo", ct, Array.Empty<object?>());
        if (info is not { Length: 6 })
        {
            return new DeviceStorageInfo(null, null, null, false, null, null);
        }

        var coverCount = Long(info[4]);
        return new DeviceStorageInfo(
            Long(info[0]),
            Long(info[1]),
            info[2].ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            },
            info[3].ValueKind == JsonValueKind.True,
            coverCount is { } count ? (int)Math.Min(count, int.MaxValue) : null,
            Long(info[5]));
    }

    public async Task<bool> RequestPersistenceAsync(CancellationToken ct = default)
    {
        var module = await GetModuleAsync(ct);
        return await module.InvokeAsync<bool>("requestPersistence", ct, Array.Empty<object?>());
    }

    public async Task EraseThisDeviceAsync(CancellationToken ct = default)
    {
        await store.EraseCurrentDatabaseAsync(ct);

        var module = await GetModuleAsync(ct);
        await module.InvokeVoidAsync("clearCoverCache", ct, Array.Empty<object?>());
    }

    public async Task ReloadAsync()
    {
        var module = await GetModuleAsync(CancellationToken.None);
        await module.InvokeVoidAsync("reload");
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

    private async ValueTask<IJSObjectReference> GetModuleAsync(CancellationToken ct)
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", ct, new object?[] { ModulePath });

    /// <summary>Byte counts from JS are doubles (quota can exceed int range); null stays null.</summary>
    private static long? Long(JsonElement value)
        => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number >= 0
            ? (long)Math.Min(number, long.MaxValue)
            : null;
}
