using Microsoft.Extensions.DependencyInjection;
using UGL.Core.Interfaces;

namespace UGL.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddDataServices(this IServiceCollection services)
    {
        services.AddSingleton<IGameRepository,          JsonGameRepository>();
        services.AddSingleton<IEmulatorRepository,      JsonEmulatorRepository>();
        services.AddSingleton<IAudioPlaylistRepository, JsonAudioPlaylistRepository>();
        return services;
    }
}
