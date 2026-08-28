using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UGL.App.ViewModels;
using UGL.App.Views;

namespace UGL.App;

/// <summary>
/// Avalonia Application subclass. Its only responsibilities are:
///   1. Initialize XAML resources.
///   2. Resolve the MainWindow from DI and show it.
///
/// It must not contain business logic or service calls — those belong
/// in ViewModels and services.
/// </summary>
public sealed partial class App : Application
{
    /// <summary>
    /// The root service provider, set by Program.Main before Avalonia starts.
    /// Exposed as a static property so that Avalonia's framework callbacks
    /// (which cannot use constructor injection) can resolve services.
    ///
    /// Outside of App.axaml.cs, prefer constructor injection over this property.
    /// </summary>
    public static IServiceProvider Services { get; internal set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Resolve the MainWindow's ViewModel from DI so it receives
            // all of its constructor-injected services.
            var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();

            var mainWindow = new MainWindow { DataContext = mainViewModel };
            desktop.MainWindow = mainWindow;

            // Shown explicitly (not left to the framework's own post-callback auto-show
            // of desktop.MainWindow) so MainWindow's OWN WindowState=FullScreen transition
            // — which Windows treats as a foreground-stealing event, the same behavior
            // WindowRestoreNative's doc comment already documents for the emulator-exit
            // case — happens BEFORE the splash window claims topmost/foreground below,
            // not after. Showing splash first and letting MainWindow go fullscreen
            // afterward was the likely reason it never became visible: MainWindow's own
            // fullscreen transition would win the foreground fight regardless of Topmost.
            mainWindow.Show();

            var splash = new StartupSplashWindow { DataContext = mainViewModel };
            splash.Show();

            // Plain Show()/Activate() was already proven unreliable against this exact
            // "something else owns Windows' foreground lock" class of problem elsewhere
            // in this app (see WindowRestoreNative) — reuse the same AttachThreadInput
            // hardening rather than trusting Activate() to actually win here.
            var hwnd = splash.TryGetPlatformHandle()?.Handle ?? 0;
            bool foregrounded = WindowRestoreNative.ForceForegroundWindow(hwnd);

            var logger = Services.GetService<ILogger<App>>();
            logger?.LogInformation("Startup splash: hwnd={Hwnd}, ForceForegroundWindow={Foregrounded}", hwnd, foregrounded);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
