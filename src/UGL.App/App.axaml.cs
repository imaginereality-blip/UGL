using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
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

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
