using Bookmarks.Core.Lookup;
using Bookmarks.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;

namespace Bookmarks.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers UI services shared by every host (PWA today, MAUI later).
    /// The host must register an <c>ILocalStore</c>.</summary>
    public static IServiceCollection AddBookmarksUI(this IServiceCollection services)
    {
        services.AddMudServices();
        services.AddScoped<LibraryService>();
        services.AddScoped<UserPreferences>();
        services.AddScoped<ThemeService>();
        services.AddScoped<AccountActions>();

        // Hosts with a service worker / sign-in register their own before calling this.
        services.TryAddScoped<IAppStatusService, NullAppStatusService>();
        services.TryAddSingleton<IAccountService, GuestAccountService>();
        services.TryAddSingleton<IGuestLibraryTransfer, NullGuestLibraryTransfer>();
        services.TryAddScoped<ISyncService, NullSyncService>();
        services.TryAddSingleton<ILocalLibraryEraser, NullLocalLibraryEraser>();
        services.TryAddScoped<ICoverColorService, NullCoverColorService>();
        services.TryAddScoped<IBarcodeScanner, NullBarcodeScanner>();
        services.TryAddScoped<IDeviceStorage, NullDeviceStorage>();
        services.TryAddScoped<IAccountDeletion, NullAccountDeletion>();
        services.TryAddScoped<IServerInfo, NullServerInfo>();

        // Book lookup (Phase 5): Open Library straight from the app; it allows CORS, so no server is involved.
        services.TryAddSingleton<IBookLookup>(_ => new OpenLibraryLookup(new HttpClient
        {
            BaseAddress = OpenLibraryLookup.DefaultBaseAddress,
            Timeout = TimeSpan.FromSeconds(20),
        }));
        return services;
    }
}
