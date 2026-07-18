using Microsoft.Extensions.DependencyInjection;
using UGL.Core.Interfaces;

namespace UGL.Media;

public static class DependencyInjection
{
    public static IServiceCollection AddMediaServices(this IServiceCollection services)
    {
        services.AddSingleton<SkiaMediaCache>();
        services.AddSingleton<IMediaCache>(sp => sp.GetRequiredService<SkiaMediaCache>());

        services.AddSingleton<MediaAssetResolver>(sp =>
        {
            var config = sp.GetRequiredService<IConfigurationService>();
            return new MediaAssetResolver(config.Settings.MediaRootPath);
        });

        services.AddSingleton<IAudioService, LibVlcAudioService>();

        services.AddSingleton<IVideoPreviewService>(sp =>
        {
            var config = sp.GetRequiredService<IConfigurationService>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<VideoPreviewService>>();
            return new VideoPreviewService(config.Settings, logger);
        });

        return services;
    }
}
