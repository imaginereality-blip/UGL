using Microsoft.Extensions.DependencyInjection;
using UGL.Core.Interfaces;

namespace UGL.Scraping;

public static class DependencyInjection
{
    public static IServiceCollection AddScrapingServices(this IServiceCollection services)
    {
        services.AddHttpClient<IgdbScraperSource>();
        services.AddHttpClient<ScreenScraperSource>();
        services.AddHttpClient<TheGamesDbScraperSource>();
        services.AddHttpClient<ComfyUiClient>();

        services.AddSingleton<IGameScraperSource>(sp => sp.GetRequiredService<IgdbScraperSource>());
        services.AddSingleton<IGameScraperSource>(sp => sp.GetRequiredService<ScreenScraperSource>());
        services.AddSingleton<IGameScraperSource>(sp => sp.GetRequiredService<TheGamesDbScraperSource>());
        services.AddSingleton<IGameScraperService, GameScraperService>();
        services.AddSingleton<IComfyUiClient>(sp => sp.GetRequiredService<ComfyUiClient>());

        return services;
    }
}
