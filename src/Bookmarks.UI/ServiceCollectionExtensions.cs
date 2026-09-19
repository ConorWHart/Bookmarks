using Bookmarks.UI.Services;
using Microsoft.Extensions.DependencyInjection;
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
        return services;
    }
}
