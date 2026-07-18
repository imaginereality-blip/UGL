using Microsoft.Extensions.DependencyInjection;
using UGL.Core.Interfaces;

namespace UGL.Configuration;

/// <summary>
/// Registers all UGL.Configuration services into the DI container.
/// Called exclusively from UGL.App's Program.cs composition root.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddConfigurationServices(this IServiceCollection services)
    {
        services.AddSingleton<IConfigurationService, JsonConfigurationService>();
        return services;
    }
}
