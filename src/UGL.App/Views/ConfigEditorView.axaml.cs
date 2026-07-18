using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UGL.App.ViewModels;

namespace UGL.App.Views;

public sealed partial class ConfigEditorView : UserControl
{
    public ConfigEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ConfigEditorViewModel vm) return;

        vm.QuitRequested += OnQuitRequested;
        vm.ScrollRequested += OnScrollRequested;

        // Centralized auto-scroll: since field navigation moves a bound property rather
        // than real Avalonia focus, each tab's ScrollViewer has no way to know it should
        // follow along. Rather than wiring this per-field in five separate files, listen
        // for any change on any tab's ViewModel and scroll whatever is currently marked
        // "fieldFocused" into view — cheap enough for a settings-sized visual tree.
        vm.Games.PropertyChanged       += (_, _) => ScrollFocusedFieldIntoView();
        vm.Systems.PropertyChanged     += (_, _) => ScrollFocusedFieldIntoView();
        vm.Audio.PropertyChanged       += (_, _) => ScrollFocusedFieldIntoView();
        vm.Theme.PropertyChanged       += (_, _) => ScrollFocusedFieldIntoView();
        vm.Paths.PropertyChanged       += (_, _) => ScrollFocusedFieldIntoView();
        vm.Peripherals.PropertyChanged += (_, _) => ScrollFocusedFieldIntoView();
    }

    /// <summary>
    /// Right-stick scroll. Each tab's top-level ScrollViewer is marked with the
    /// "contentScroll" class specifically so this can find it without accidentally
    /// grabbing a ListBox's own internal scrollviewer (every ListBox has one from its
    /// default control template, and it would otherwise be found first in a tree walk).
    /// </summary>
    private const double ScrollStepPixels = 48;

    private void OnScrollRequested(int direction)
    {
        ScrollViewer? Find(Visual visual)
        {
            if (visual is ScrollViewer { Classes: { } classes } sv &&
                classes.Contains("contentScroll") && sv.IsEffectivelyVisible)
                return sv;

            foreach (var child in visual.GetVisualChildren())
            {
                if (Find(child) is { } found) return found;
            }
            return null;
        }

        var scrollViewer = Find(this);
        if (scrollViewer is null) return;

        var offset = scrollViewer.Offset;
        scrollViewer.Offset = new Vector(offset.X, offset.Y + direction * ScrollStepPixels);
    }

    private void ScrollFocusedFieldIntoView()
    {
        // Deferred via Dispatcher: if a field just became visible in this same call stack
        // (e.g. IsSystemEditorOpen flipping true), it hasn't been measured/arranged yet —
        // calling BringIntoView() synchronously here can silently no-op or scroll to a
        // stale position. Posting ensures this runs after the pending layout pass.
        Dispatcher.UIThread.Post(() =>
        {
            Control? Find(Visual visual)
            {
                if (visual is Control { Classes: { } classes } control && classes.Contains("fieldFocused"))
                    return control;

                foreach (var child in visual.GetVisualChildren())
                {
                    if (Find(child) is { } found) return found;
                }
                return null;
            }

            Find(this)?.BringIntoView();
        });
    }

    /// <summary>
    /// A mouse click directly on the Quit row is inherently deliberate — unlike D-pad
    /// navigation, you don't "pass through" a row by clicking it — so it confirms Quit
    /// immediately rather than requiring a separate confirm step.
    /// </summary>
    private void OnMenuItemTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ConfigEditorViewModel vm &&
            e.Source is Control { DataContext: SettingsMenuItem { IsQuit: true } })
        {
            vm.ConfirmQuit();
        }
    }

    /// <summary>
    /// Exits the whole application. Only called when Quit is explicitly confirmed
    /// (see ConfigEditorViewModel.ConfirmQuit) — never merely from highlighting it.
    /// </summary>
    private static void OnQuitRequested()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window?.DataContext is MainWindowViewModel mwvm)
            mwvm.CloseSettings();
    }
}
