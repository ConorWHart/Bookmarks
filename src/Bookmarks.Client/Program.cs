using Bookmarks.Client;
using Bookmarks.Core.Storage;
using Bookmarks.UI;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Phase 2 swaps this for the IndexedDB-backed store.
builder.Services.AddSingleton<ILocalStore, InMemoryLocalStore>();

builder.Services.AddBookmarksUI();

await builder.Build().RunAsync();
