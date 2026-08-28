using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using UGL.App.ViewModels;

namespace UGL.App.Views;

/// <summary>
/// Closes itself the instant MainWindowViewModel.IsShowingStartupVideo flips false —
/// see StartupVideoView (this window's sole content) for what flips it: the video
/// ending, or any controller/keyboard input via MainWindowViewModel.
/// HandleControllerInputAsync's top-priority skip check.
/// </summary>
public sealed partial class StartupSplashWindow : Window
{
    public StartupSplashWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += (_, _) => LogStatus("Opened");
        Closing += (_, _) => LogStatus("Closing");
    }

    private void LogStatus(string what) =>
        (App.Services.GetService(typeof(ILogger<StartupSplashWindow>)) as ILogger<StartupSplashWindow>)
        ?.LogInformation("StartupSplashWindow.{What}: WindowState={State}, Bounds={Bounds}, IsVisible={Visible}",
            what, WindowState, Bounds, IsVisible);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        // Cover the already-false case (shouldn't happen at startup, but guards
        // against this window somehow attaching after the flag already flipped).
        if (!vm.IsShowingStartupVideo) Dispatcher.UIThread.Post(CloseAfterStoppingPlayback);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsShowingStartupVideo)) return;
        if (sender is MainWindowViewModel { IsShowingStartupVideo: false })
            Dispatcher.UIThread.Post(CloseAfterStoppingPlayback);
    }

    /// <summary>
    /// Stops and disposes the LibVLC player BEFORE closing the window, not during it
    /// — see StartupVideoView.StopPlayback's doc comment for the native crash this
    /// ordering fixes.
    /// </summary>
    private void CloseAfterStoppingPlayback()
    {
        this.FindControl<StartupVideoView>("Video")?.StopPlayback();
        Close();
    }
}
