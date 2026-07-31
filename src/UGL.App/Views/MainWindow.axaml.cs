using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using UGL.App.ViewModels;
using UGL.Input;
using Window = Avalonia.Controls.Window;

namespace UGL.App.Views;

/// <summary>
/// Code-behind for the main application window.
/// Responsibilities:
///   1. Trigger async ViewModel initialization after the visual tree loads.
///   2. Forward keyboard events to KeyboardInputService.
///   3. Start/stop the XInput polling service with window lifetime.
///   4. Sync overlay IsVisible properties with their ViewModel flags.
///   5. Minimize/restore the window around emulator sessions.
/// </summary>
public sealed partial class MainWindow : Window
{
    private KeyboardInputService? _keyboard;
    private UGL.Core.Interfaces.IInputService? _inputService;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _keyboard     = App.Services.GetRequiredService<KeyboardInputService>();
        _inputService = App.Services.GetRequiredService<UGL.Core.Interfaces.IInputService>();
        _inputService.Start();

        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(MainWindowViewModel.IsFilterOverlayOpen):
                        if (this.FindControl<FilterOverlayView>("FilterOverlay") is { } fo)
                            fo.IsVisible = vm.IsFilterOverlayOpen;
                        break;

                    case nameof(MainWindowViewModel.IsSettingsOpen):
                        if (this.FindControl<ConfigEditorView>("ConfigEditor") is { } ce)
                            ce.IsVisible = vm.IsSettingsOpen;
                        break;

                    case nameof(MainWindowViewModel.IsEmulatorRunning):
                        // MainWindowViewModel.OnEmulatorExited (the false case) is invoked
                        // directly from ProcessEmulatorLauncher.OnProcessExited, which runs
                        // on a raw ThreadPool thread via Process.Exited — not the UI thread.
                        // Touching WindowState/Activate/Focus from there throws
                        // "Call from invalid thread" and crashes the whole process (this was
                        // a real, reproduced crash, not a hypothetical). The true case is
                        // already always raised from the UI thread (OnGameConfirmed runs off
                        // controller input, itself already dispatched), but routing both
                        // through the same Post keeps this correct regardless of what calls
                        // the setter next.
                        bool running = vm.IsEmulatorRunning;
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (running)
                            {
                                WindowState = WindowState.Minimized;
                            }
                            else
                            {
                                WindowState = WindowState.FullScreen;
                                Activate();
                                Focus();
                            }
                        });
                        break;
                }
            };

            await vm.InitializeAsync();
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _inputService?.Stop();
        base.OnUnloaded(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Skip the controller-simulation shortcuts entirely while a text-entry control
        // has real focus. This mapping (E, Space, arrows, etc.) has always fired
        // regardless of focus, but it only became actively disruptive once Settings
        // grew real per-field behavior — Select now opens the on-screen keyboard, so
        // pressing Space to type a literal space also pops it open mid-typing, and the
        // event being marked handled here can prevent the character from reaching the
        // focused control at all.
        var focused = FocusManager?.GetFocusedElement();
        if (focused is TextBox or NumericUpDown or AutoCompleteBox)
            return;

        if (_keyboard is not null)
            e.Handled = _keyboard.HandleKeyDown(e.Key);
    }
}
