using Microsoft.Extensions.DependencyInjection;
using UGL.Core.Interfaces;

namespace UGL.Themes;

public static class DependencyInjection
{
    public static IServiceCollection AddThemeServices(this IServiceCollection services)
    {
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        return services;
    }
}
