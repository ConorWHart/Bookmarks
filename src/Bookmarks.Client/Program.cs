using Bookmarks.Client;
using Bookmarks.Client.Api;
using Bookmarks.Client.Auth;
using Bookmarks.Client.Covers;
using Bookmarks.Client.Pwa;
using Bookmarks.Client.Storage;
using Bookmarks.Client.Sync;
using Bookmarks.Core.Storage;
using Bookmarks.Core.Sync;
using Bookmarks.UI;
using Bookmarks.UI.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = baseAddress });

// Sign-in settings (wwwroot/appsettings.json). Read key by key: the reflection-based binder
// doesn't survive trimming in WebAssembly.
var auth = new ClientAuthOptions
{
    Authority = builder.Configuration["Auth:Authority"] ?? "",
    KnownAuthority = builder.Configuration["Auth:KnownAuthority"] ?? "",
    ClientId = builder.Configuration["Auth:ClientId"] ?? "",
    ApiScope = builder.Configuration["Auth:ApiScope"] ?? "",
    RedirectUri = new Uri(baseAddress, (builder.Configuration["Auth:RedirectPath"] ?? "/authentication/login-callback").TrimStart('/')).ToString(),
};
builder.Services.AddSingleton(auth);

// The API lives on another origin (localhost:7201 in development, App Service once deployed).
var apiBaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? baseAddress.ToString());

// Longer than the default 100 s: after a quiet spell the API starts cold and the database resumes from
// auto-pause, and the first sync can take over a minute (the API retries for ~90 s, DataSetup.cs).
var apiTimeout = TimeSpan.FromSeconds(150);

// Sign-in: MSAL for Entra, plus our API's device sessions (its own HttpClient for /api/sessions).
builder.Services.AddSingleton(sp => new BrowserAccountService(
    sp.GetRequiredService<IJSRuntime>(),
    auth,
    new HttpClient { BaseAddress = apiBaseAddress, Timeout = apiTimeout },
    sp.GetRequiredService<ILogger<BrowserAccountService>>()));
builder.Services.AddSingleton<IAccountService>(sp => sp.GetRequiredService<BrowserAccountService>());

// IndexedDB via Dexie (wwwroot/js/localStore.js). One database per account; the name is set below,
// before anything opens it.
builder.Services.AddSingleton<IndexedDbLocalStore>();
builder.Services.AddSingleton<ILocalStore>(sp => sp.GetRequiredService<IndexedDbLocalStore>());
builder.Services.AddSingleton<IGuestLibraryTransfer>(sp => sp.GetRequiredService<IndexedDbLocalStore>());
builder.Services.AddSingleton<ILocalLibraryEraser>(sp => sp.GetRequiredService<IndexedDbLocalStore>());

// Online/offline and "new version ready" via the service worker (js/pwa.js).
builder.Services.AddScoped<IAppStatusService, BrowserAppStatusService>();

// Cover colors for spines (Phase 5): sampled on a canvas (js/covers.js).
builder.Services.AddScoped<ICoverColorService, BrowserCoverColorService>();

// Barcode scanning (Phase 5): BarcodeDetector where the browser has it, else ZXing (js/scanner.js).
builder.Services.AddScoped<IBarcodeScanner, BrowserBarcodeScanner>();

builder.Services.AddScoped(sp => new BookmarksApiClient(
    new HttpClient { BaseAddress = apiBaseAddress, Timeout = apiTimeout },
    sp.GetRequiredService<BrowserAccountService>()));

// Sync (Phase 4): the engine pushes the outbox and pulls server changes through the API client;
// BrowserSyncService decides when (startup, after edits, back online, periodically, Sync now).
builder.Services.AddScoped<ISyncTransport>(sp => sp.GetRequiredService<BookmarksApiClient>());
builder.Services.AddScoped(sp => new SyncEngine(
    sp.GetRequiredService<ILocalStore>(),
    sp.GetRequiredService<ISyncTransport>(),
    TimeProvider.System));
builder.Services.AddScoped<BrowserSyncService>();
builder.Services.AddScoped<ISyncService>(sp => sp.GetRequiredService<BrowserSyncService>());

// Settings (Phase 7a): delete account (DELETE /api/me), the API's version, this device's storage (js/device.js).
builder.Services.AddScoped<IAccountDeletion, BrowserAccountDeletion>();
builder.Services.AddScoped<IServerInfo>(sp => sp.GetRequiredService<BookmarksApiClient>());
builder.Services.AddScoped<IDeviceStorage, BrowserDeviceStorage>();

// Backups and exports (Phase 7b): browser downloads (js/files.js).
builder.Services.AddScoped<IFileSaver, BrowserFileSaver>();

builder.Services.AddBookmarksUI();

var host = builder.Build();

// Restore the signed-in account from MSAL's cache (no network), then point the local store at that
// account's database. Guests keep using "bookmarks", exactly as before sign-in existed.
var accounts = host.Services.GetRequiredService<BrowserAccountService>();
await accounts.InitializeAsync();
host.Services.GetRequiredService<IndexedDbLocalStore>().DatabaseName =
    IndexedDbLocalStore.DatabaseNameFor(accounts.Current.UserId);

await host.RunAsync();
