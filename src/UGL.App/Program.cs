using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UGL.App.ViewModels;
using UGL.Configuration;
using UGL.Data;
using UGL.Emulators;
using UGL.Input;
using UGL.Media;
using UGL.Scraping;
using UGL.Themes;

namespace UGL.App;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        // Write any unhandled crash to a file next to the exe
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var crashPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.WriteAllText(crashPath, e.ExceptionObject?.ToString() ?? "Unknown error");
            Console.Error.WriteLine("CRASH: " + e.ExceptionObject);
        };

        // Ensure the full portable folder layout exists before anything else runs —
        // config repositories, media loading, etc. all assume these folders are
        // already there rather than creating them defensively themselves.
        AppFolderScaffolder.EnsureFolders();

        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "ugl.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    // FileLoggerProvider exists specifically so logs are readable while
                    // the fullscreen window covers the console — it was implemented but
                    // never actually registered here, so logs\ugl.log never got written
                    // and runtime failures (e.g. a rejected ComfyUI workflow) had no
                    // record anywhere.
                    logging.AddProvider(new FileLoggerProvider(logPath));
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices((_, services) => RegisterServices(services))
                .Build();

            var configuration = host.Services.GetRequiredService<UGL.Core.Interfaces.IConfigurationService>();
            await configuration.InitializeAsync();

            // PeripheralRegistry.SaveAsync() has always persisted scanned controllers
            // to config/controllers.json correctly — but nothing ever called the
            // matching LoadAsync() at startup, so KnownDevices started empty every
            // run regardless of what was already saved on disk, requiring a rescan
            // every session.
            var peripheralRegistry = host.Services.GetRequiredService<UGL.Core.Interfaces.IPeripheralRegistry>();
            await peripheralRegistry.LoadAsync();

            App.Services = host.Services;

            await host.StartAsync();

            int exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            await host.StopAsync();
            host.Dispose();

            return exitCode;
        }
        catch (Exception ex)
        {
            var crashPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.WriteAllText(crashPath, ex.ToString());
            Console.Error.WriteLine("FATAL STARTUP ERROR:");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddConfigurationServices();
        services.AddDataServices();
        services.AddMediaServices();
        services.AddInputServices();
        services.AddEmulatorServices();

        // Registered directly here rather than inside AddDataServices() (which
        // already registers IAudioPlaylistRepository), since that extension method
        // lives in a file not touched by this change — functionally equivalent.
        services.AddSingleton<UGL.Core.Interfaces.IAudioTrackRepository, UGL.Data.JsonAudioTrackRepository>();
        services.AddThemeServices();

        // HomeMenuViewModel and GameBrowserViewModel are Singleton: MainWindowViewModel
        // owns one instance of each for the lifetime of the app and swaps CurrentView
        // between them rather than recreating the Game Browser on every navigation.
        services.AddSingleton<HomeMenuViewModel>();
        services.AddSingleton<GameBrowserViewModel>();
        services.AddSingleton<FilterOverlayViewModel>();

        // Config editor tab ViewModels
        services.AddSingleton<UGL.App.ViewModels.Config.CategoriesConfigViewModel>();
        services.AddSingleton<UGL.App.ViewModels.Config.GamesConfigViewModel>();
        services.AddSingleton<UGL.App.ViewModels.Config.SystemsConfigViewModel>();
        services.AddSingleton<UGL.App.ViewModels.Config.AudioConfigViewModel>();
        services.AddSingleton<UGL.App.ViewModels.Config.ThemeConfigViewModel>();
        services.AddSingleton<UGL.App.ViewModels.Config.CardHighlightConfigViewModel>();
        services.AddSingleton<UGL.App.ViewModels.Config.TitleGraphicsConfigViewModel>();
        services.AddSingleton<UGL.App.ViewModels.Config.PathsConfigViewModel>();
        services.AddSingleton<UGL.App.ViewModels.Config.PeripheralConfigViewModel>();
        services.AddSingleton<VirtualKeyboardViewModel>();
        services.AddSingleton<ConfirmDialogViewModel>();
        services.AddSingleton<ConfigEditorViewModel>();

        // Hook integration (MameHooker / Hook of the Reaper) — registered directly here
        // rather than via the AddDataServices()/AddEmulatorServices() extension methods,
        // since those live in files not touched by this change; functionally equivalent.
        services.AddSingleton<UGL.Core.Interfaces.IHookSettingsRepository, JsonHookSettingsRepository>();
        services.AddSingleton<UGL.Hooks.IHookLauncher, UGL.Hooks.HookLauncher>();
        services.AddSingleton<UGL.App.ViewModels.Config.HookConfigViewModel>();

        services.AddSingleton<UGL.Core.Interfaces.IUpdateService, UGL.Updates.GitHubUpdateService>();
        services.AddSingleton<UGL.App.ViewModels.Config.UpdateConfigViewModel>();

        // Scraper (IGDB / ScreenScraper / TheGamesDB) + ComfyUI card generation
        services.AddSingleton<UGL.Core.Interfaces.IScraperSettingsRepository, JsonScraperSettingsRepository>();
        services.AddScrapingServices();
        services.AddSingleton<UGL.App.ViewModels.Config.ScraperConfigViewModel>();

        services.AddTransient<MainWindowViewModel>();
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .LogToTrace();
}
