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

        // Hosts with a service worker register their own before calling this.
        services.TryAddScoped<IAppStatusService, NullAppStatusService>();
        return services;
    }
}
