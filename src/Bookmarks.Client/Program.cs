using Bookmarks.Client;
using Bookmarks.Client.Pwa;
using Bookmarks.Client.Storage;
using Bookmarks.Core.Storage;
using Bookmarks.UI;
using Bookmarks.UI.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// IndexedDB via Dexie (wwwroot/js/localStore.js). The database opens on first use.
builder.Services.AddSingleton<ILocalStore, IndexedDbLocalStore>();

// Online/offline and "new version ready" via the service worker (js/pwa.js).
builder.Services.AddScoped<IAppStatusService, BrowserAppStatusService>();

builder.Services.AddBookmarksUI();

await builder.Build().RunAsync();
