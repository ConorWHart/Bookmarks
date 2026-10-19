using Microsoft.AspNetCore.Components;

namespace Bookmarks.UI.Services;

/// <summary>
/// Reads book barcodes (ISBN-13) from the device camera. The PWA uses <c>js/scanner.js</c>; hosts without a camera
/// API get <see cref="NullBarcodeScanner"/> and the scan button stays disabled.
/// </summary>
public interface IBarcodeScanner
{
    /// <summary>A camera API is available here (secure context, getUserMedia).</summary>
    Task<bool> IsSupportedAsync();

    /// <summary>
    /// Starts the camera into <paramref name="video"/> and scans until a valid ISBN is found or <see cref="StopAsync"/>.
    /// Returns null when running, or an error code: <c>denied</c>, <c>nocamera</c>, <c>inuse</c>, <c>unsupported</c>,
    /// <c>nodecoder</c>, <c>unknown</c>.
    /// </summary>
    /// <remarks>
    /// With <paramref name="continuous"/> (scan a stack), scanning carries on after each ISBN: a short pause, a beep,
    /// and the same code isn't reported again while it's still in view.
    /// </remarks>
    Task<string?> StartAsync(ElementReference video, IBarcodeScanHandler handler, bool continuous = false);

    Task StopAsync();

    Task<bool> TorchAvailableAsync();

    Task<bool> SetTorchAsync(bool on);
}

/// <summary>Receives scan results. Scanning stops after <see cref="OnDetectedAsync"/>.</summary>
public interface IBarcodeScanHandler
{
    /// <summary>A valid ISBN-13 was read.</summary>
    Task OnDetectedAsync(string isbn13);

    /// <summary>A barcode was read but it isn't an ISBN (a price or store code). Throttled.</summary>
    Task OnNotIsbnAsync();
}

public sealed class NullBarcodeScanner : IBarcodeScanner
{
    public Task<bool> IsSupportedAsync() => Task.FromResult(false);

    public Task<string?> StartAsync(ElementReference video, IBarcodeScanHandler handler, bool continuous = false)
        => Task.FromResult<string?>("unsupported");

    public Task StopAsync() => Task.CompletedTask;

    public Task<bool> TorchAvailableAsync() => Task.FromResult(false);

    public Task<bool> SetTorchAsync(bool on) => Task.FromResult(false);
}
