using Microsoft.Extensions.DependencyInjection;
using UGL.Core.Interfaces;

namespace UGL.Emulators;

public static class DependencyInjection
{
    public static IServiceCollection AddEmulatorServices(this IServiceCollection services)
    {
        services.AddSingleton<IRetroArchConfigGenerator, RetroArchConfigGenerator>();
        services.AddSingleton<IEmulatorLauncher, ProcessEmulatorLauncher>();
        return services;
    }
}
