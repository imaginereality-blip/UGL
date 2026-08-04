using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;

namespace UGL.App.ViewModels;

/// <summary>
/// Root ViewModel for the application window.
/// Owns the two-level navigation stack and routes controller input to
/// whichever level (HomeMenuViewModel or GameBrowserViewModel) is active.
///
/// This replaces the Milestone 3 version, which always routed input
/// directly to a single DashboardViewModel. The InputReceived subscription
/// and constructor-injection pattern are otherwise unchanged.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IConfigurationService _configuration;
    private readonly IThemeService _themeService;
    private readonly IInputService _inputService;
    private readonly IAudioService _audioService;
    private readonly IEmulatorLauncher _launcher;
    private readonly IUpdateService _updateService;
    private readonly ILogger<MainWindowViewModel> _logger;

    private readonly HomeMenuViewModel _homeMenu;
    private readonly GameBrowserViewModel _gameBrowser;
    private readonly FilterOverlayViewModel _filterOverlay;
    private readonly ConfigEditorViewModel _configEditor;

    [ObservableProperty] private string _windowTitle = "UltimateGameLauncher";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private object _currentView = null!;
    [ObservableProperty] private bool _isFilterOverlayOpen;
    [ObservableProperty] private bool _isSettingsOpen;

    // ── "Now playing" toast — brief indicator shown when the music playlist is
    // manually cycled via LB/RB, auto-hidden after a few seconds. ─────────────
    [ObservableProperty] private string _nowPlayingPlaylistName = string.Empty;
    [ObservableProperty] private bool _isNowPlayingVisible;
    private DispatcherTimer? _nowPlayingHideTimer;
    [ObservableProperty] private bool _isEmulatorRunning;
    [ObservableProperty] private string _launchError = string.Empty;

    // ── Update notification — shown after a background check finds a new version.
    // Unlike the "now playing" toast, this stays visible (not auto-hidden) until the
    // user opens Settings and sees the Updates tab, since it's more actionable and
    // easy to miss if it vanished after a few seconds. ─────────────────────────────
    [ObservableProperty] private bool _isUpdateNotificationVisible;
    [ObservableProperty] private string _updateNotificationText = string.Empty;

    public FilterOverlayViewModel FilterOverlay => _filterOverlay;
    public ConfigEditorViewModel  ConfigEditor  => _configEditor;

    public MainWindowViewModel(
        IConfigurationService configuration,
        IThemeService themeService,
        IInputService inputService,
        IAudioService audioService,
        IEmulatorLauncher launcher,
        IUpdateService updateService,
        HomeMenuViewModel homeMenu,
        GameBrowserViewModel gameBrowser,
        FilterOverlayViewModel filterOverlay,
        ConfigEditorViewModel configEditor,
        ILogger<MainWindowViewModel> logger)
    {
        _configuration = configuration;
        _themeService = themeService;
        _inputService = inputService;
        _audioService = audioService;
        _launcher = launcher;
        _updateService = updateService;
        _logger = logger;

        _homeMenu = homeMenu;
        _gameBrowser = gameBrowser;
        _filterOverlay = filterOverlay;
        _configEditor = configEditor;

        _homeMenu.CategoryConfirmed += OnCategoryConfirmedAsync;
        _gameBrowser.BackRequested += OnBackRequested;
        _gameBrowser.GameConfirmed += OnGameConfirmed;

        _filterOverlay.FilterApplied += OnFilterApplied;
        _filterOverlay.Dismissed += OnFilterDismissed;

        _configEditor.Games.GameCatalogChanged += OnGameCatalogChanged;

        _launcher.EmulatorExited += OnEmulatorExited;

        _audioService.PlaylistChanged += OnPlaylistChanged;
        _audioService.TrackChanged += OnTrackChanged;
        _updateService.UpdateAvailable += OnUpdateAvailable;

        CurrentView = _homeMenu;

        _inputService.InputReceived += OnControllerInput;
        _logger.LogDebug("MainWindowViewModel initialized.");
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            await _themeService.ApplyThemeAsync(_configuration.Settings.ActiveThemeId);

            // Load before HomeMenu.InitializeAsync() so the very first render of category
            // cards already reflects whatever was previously saved, not just the static
            // default — CardHighlightConfigViewModel only loads this when Settings is
            // opened, which may never happen in a given session.
            var s = _configuration.Settings;
            CardHighlightSettings.Load(s.CardHighlightColor, s.CardHighlightIntensity, s.CardHighlightStyle, s.CardHighlightThickness);

            await _homeMenu.InitializeAsync();
            await _audioService.StartAsync();

            // Non-blocking — never delay startup on a network round-trip, and never
            // let a failed check (no internet, GitHub down, etc.) be disruptive.
            _updateService.CheckForUpdateInBackground();

            _logger.LogInformation(
                "UGL started. Active theme: {ThemeId}",
                _configuration.Settings.ActiveThemeId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Navigation: Home Menu → Game Browser → Home Menu ──────────────────

    private async Task OnCategoryConfirmedAsync(Category category)
    {
        _logger.LogInformation("Navigating to Game Browser for category: {Category}", category.Label);
        _audioService.PlayConfirm();
        await _gameBrowser.SetCategoryAsync(category);
        _ = _audioService.SwitchPlaylistAsync(category.Id);
        CurrentView = _gameBrowser;
    }

    private void OnBackRequested()
    {
        _logger.LogInformation("Returning to Home Menu");
        _audioService.PlayBack();
        _ = _homeMenu.RestoreSelection();
        _ = _audioService.SwitchPlaylistAsync("global");
        CurrentView = _homeMenu;
    }

    private async void OnGameConfirmed(Game game)
    {
        if (_launcher.IsEmulatorRunning)
        {
            _logger.LogWarning("Launch ignored — emulator already running.");
            return;
        }

        _logger.LogInformation("Launching: {Title}", game.Title);
        LaunchError = string.Empty;
        _audioService.Pause();

        // Signal the View to minimize — handled in MainWindow.axaml.cs
        IsEmulatorRunning = true;

        // Update last-played timestamp
        game.LastPlayed = DateTimeOffset.UtcNow;

        var pid = await _launcher.LaunchGameAsync(game);
        if (pid < 0)
        {
            IsEmulatorRunning = false;
            _audioService.Resume();
            LaunchError = $"Failed to launch '{game.Title}'. Check the emulator path in Settings.";
            _logger.LogError("LaunchGameAsync returned -1 for {Title}", game.Title);
        }
    }

    private void OnEmulatorExited(object? sender, int exitCode)
    {
        _logger.LogInformation("Emulator exited (code {Code}). Restoring UGL.", exitCode);
        IsEmulatorRunning = false;
        _audioService.Resume();
    }

    // ── "Now playing" toast ─────────────────────────────────────────────────

    private void OnPlaylistChanged(string playlistName) => ShowNowPlayingToast(playlistName);

    private void OnTrackChanged(string trackName) => ShowNowPlayingToast(trackName);

    private void ShowNowPlayingToast(string text)
    {
        NowPlayingPlaylistName = text;
        IsNowPlayingVisible = true;

        _nowPlayingHideTimer?.Stop();
        _nowPlayingHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _nowPlayingHideTimer.Tick += (_, _) =>
        {
            IsNowPlayingVisible = false;
            _nowPlayingHideTimer?.Stop();
            _nowPlayingHideTimer = null;
        };
        _nowPlayingHideTimer.Start();
    }

    /// <summary>Which category's playlist should be eligible for LB/RB cycling
    /// alongside Global, depending on where we currently are — null if at the Home
    /// Menu with nothing meaningfully selected yet (shouldn't normally happen once
    /// categories exist, but handled defensively).</summary>
    /// <summary>Which category's playlist should be eligible for LB/RB cycling
    /// alongside Global, depending on where we currently are. At the Home Menu this
    /// is always null — browsing left/right past category cards only highlights
    /// them, it doesn't mean you're "in" that category the way actually entering
    /// its Game Browser does, so a category's playlist shouldn't be cyclable just
    /// because its card happens to be highlighted.</summary>
    private string? CurrentCategoryIdForPlaylistCycling(bool atHome) =>
        atHome ? null : _gameBrowser.ActiveCategory?.Id;

    // ── Update notification ─────────────────────────────────────────────────

    private void OnUpdateAvailable(UpdateCheckResult update)
    {
        UpdateNotificationText = $"Update available: {update.LatestVersion} — see Settings";
        IsUpdateNotificationVisible = true;
    }

    // ── Filter overlay ─────────────────────────────────────────────────────

    private async void OnOpenFilter()
    {
        var category = _gameBrowser.ActiveCategory;
        if (category is null) return;

        await _filterOverlay.OpenAsync(
            category.Id,
            category.Label,
            _gameBrowser.ActiveFilter);

        IsFilterOverlayOpen = true;
        _logger.LogInformation("Filter overlay opened for category: {Category}", category.Label);
    }

    private async void OnFilterApplied(GameFilter filter)
    {
        IsFilterOverlayOpen = false;

        if (_gameBrowser.ActiveCategory is { } category)
            await _gameBrowser.SetCategoryAsync(category, filter);

        CurrentView = _gameBrowser;
        _logger.LogInformation(
            "Filter applied — System:{System} Genre:{Genre} Players:{Players}",
            filter.SystemId ?? "All", filter.Genre ?? "All", filter.Players?.ToString() ?? "All");
    }

    private void OnFilterDismissed()
    {
        IsFilterOverlayOpen = false;
        _logger.LogInformation("Filter overlay dismissed.");
    }

    // ── Settings ───────────────────────────────────────────────────────────

    private async void OnOpenSettings()
    {
        await _configEditor.InitializeAsync();
        _audioService.Pause();
        IsSettingsOpen = true;
        IsUpdateNotificationVisible = false;
        _logger.LogInformation("Settings opened.");
    }

    public void CloseSettings()
    {
        IsSettingsOpen = false;
        _audioService.Resume();
        _logger.LogInformation("Settings closed.");

        // Reload category art paths from config and refresh visible cards.
        // This picks up any ArtPath changes made in Settings immediately.
        _ = _homeMenu.RefreshCategoriesAsync();

        // A category added, renamed, or removed in the Categories tab needs to show up
        // in the Games editor's category checkboxes too, without reopening Settings.
        _ = _configEditor.Games.RefreshCategoriesAsync();
    }

    private void OnGameCatalogChanged()
    {
        // If the game browser is showing, refresh it with the current category.
        if (_gameBrowser.ActiveCategory is { } cat)
            _ = _gameBrowser.SetCategoryAsync(cat, _gameBrowser.ActiveFilter);
    }

    // ── Controller/keyboard input dispatch ─────────────────────────────────

    private async void OnControllerInput(object? sender, ControllerInputEvent e)
    {
        // async void is unavoidable here — this is a genuine event handler, and the
        // standard .NET event model has no Task-returning equivalent. What it does
        // mean is an unhandled exception here can crash the whole process rather than
        // just failing one input action, so the actual logic is wrapped in try/catch
        // rather than left bare.
        try
        {
            await HandleControllerInputAsync(e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception handling controller input (action: {Action}).", e.Action);
        }
    }

    private async Task HandleControllerInputAsync(ControllerInputEvent e)
    {
        _logger.LogDebug("Controller [{Index}] action: {Action}", e.ControllerIndex, e.Action);

        // Quit-to-launcher (LB+RB+Start+Back) takes priority over everything, including
        // the virtual keyboard/Settings checks below — it's meaningful only while a game
        // is actually running, at which point none of that other UI state matters anyway.
        if (e.Action == ControllerAction.QuitToLauncher)
        {
            if (_launcher.IsEmulatorRunning)
            {
                _logger.LogInformation("Quit-to-launcher combo detected — killing emulator.");
                await _launcher.KillCurrentEmulatorAsync();
            }
            return;
        }

        // While a game/emulator is running, UGL is minimized (see OnGameConfirmed/
        // MainWindow.axaml.cs) and shouldn't act on any other input — polling continues
        // in the background purely so the quit combo above can be detected regardless of
        // which window has focus. Without this guard, held navigation/Settings/etc. input
        // would still be processed against a hidden UI.
        if (_launcher.IsEmulatorRunning) return;

        // The delete-confirmation dialog, when open, takes top priority over
        // everything else — same reasoning as the virtual keyboard below, and
        // checked first since a destructive-action confirm should never be
        // interruptible by anything else demanding input.
        var confirmDialog = _configEditor.ConfirmDialog;
        if (confirmDialog.IsOpen)
        {
            switch (e.Action)
            {
                case ControllerAction.NavigateLeft:  confirmDialog.NavigateLeft();  break;
                case ControllerAction.NavigateRight: confirmDialog.NavigateRight(); break;
                case ControllerAction.Select:        confirmDialog.Confirm();      break;
                case ControllerAction.Back:          confirmDialog.Cancel();       break;
            }

            return;
        }

        // The virtual keyboard, when open, takes top priority over everything else —
        // including Settings itself — until Done or Cancel closes it.
        var keyboard = _configEditor.VirtualKeyboard;
        if (keyboard.IsOpen)
        {
            switch (e.Action)
            {
                case ControllerAction.NavigateUp:    keyboard.NavigateUp();    break;
                case ControllerAction.NavigateDown:  keyboard.NavigateDown();  break;
                case ControllerAction.NavigateLeft:  keyboard.NavigateLeft();  break;
                case ControllerAction.NavigateRight: keyboard.NavigateRight(); break;
                case ControllerAction.Select:        keyboard.Confirm();      break;
                case ControllerAction.Back:          keyboard.Cancel();       break;
            }

            return;
        }

        // If settings are open, route input based on whether the sidebar menu or a
        // tab's content currently has focus (see ConfigEditorViewModel.IsContentFocused).
        if (IsSettingsOpen)
        {
            switch (e.Action)
            {
                case ControllerAction.Back:
                    if (_configEditor.IsContentFocused)
                    {
                        // Give the active tab a chance to close just its own nested
                        // sub-mode first (a checkbox grid, a track sub-list) — only
                        // fall through to fully exiting content if there wasn't one.
                        if (!_configEditor.TryHandleContentBack())
                            _configEditor.ExitContent();
                    }
                    else
                        CloseSettings();
                    break;

                case ControllerAction.Start:
                    // Start always fully closes, regardless of nested state — it's the
                    // "toggle Settings closed" button, not a step-back button.
                    if (_configEditor.IsContentFocused)
                        _configEditor.ExitContent();
                    else
                        CloseSettings();
                    break;

                case ControllerAction.NavigateUp:
                    if (_configEditor.IsContentFocused) _configEditor.NavigateContentUp();
                    else _configEditor.NavigateMenuUp();
                    break;

                case ControllerAction.NavigateDown:
                    if (_configEditor.IsContentFocused) _configEditor.NavigateContentDown();
                    else _configEditor.NavigateMenuDown();
                    break;

                case ControllerAction.NavigateLeft:
                    if (_configEditor.IsContentFocused) _configEditor.NavigateContentLeft();
                    break;

                case ControllerAction.NavigateRight:
                    // Deliberately no else-branch: entering a sidebar tab's content
                    // requires an explicit Select, same as NavigateLeft above. This
                    // used to call EnterContent() here, which meant any rightward
                    // stick deflection (trivially easy to trigger while actually
                    // trying to move Up/Down) immediately opened whatever tab was
                    // highlighted — the real cause of reported "stick drift".
                    if (_configEditor.IsContentFocused) _configEditor.NavigateContentRight();
                    break;

                case ControllerAction.Select:
                    if (_configEditor.IsContentFocused) await _configEditor.ConfirmContentAsync();
                    else _configEditor.ConfirmMenuSelection();
                    break;

                case ControllerAction.ScrollUp:
                    _configEditor.ScrollContent(-1);
                    break;

                case ControllerAction.CategoryLeft:
                    _configEditor.SwitchSubTabLeft();
                    break;

                case ControllerAction.CategoryRight:
                    _configEditor.SwitchSubTabRight();
                    break;

                case ControllerAction.Secondary:
                    _configEditor.QuickAddInContent();
                    break;

                case ControllerAction.ScrollDown:
                    _configEditor.ScrollContent(1);
                    break;
            }

            return;
        }

        bool atHome = ReferenceEquals(CurrentView, _homeMenu);

        // If filter overlay is open, route all input there instead.
        if (IsFilterOverlayOpen)
        {
            switch (e.Action)
            {
                case ControllerAction.NavigateUp:    _filterOverlay.NavigateUp();    break;
                case ControllerAction.NavigateDown:  _filterOverlay.NavigateDown();  break;
                case ControllerAction.NavigateLeft:  _filterOverlay.NavigateLeft();  break;
                case ControllerAction.NavigateRight: _filterOverlay.NavigateRight(); break;
                case ControllerAction.Select:        _filterOverlay.Confirm();       break;
                case ControllerAction.Back:          _filterOverlay.Dismiss();       break;
                case ControllerAction.Secondary:     _filterOverlay.ResetAll();      break;
            }
            return;
        }

        switch (e.Action)
        {
            case ControllerAction.NavigateRight:
                _audioService.PlayNavigate();
                if (atHome) await _homeMenu.NavigateRightAsync();
                else await _gameBrowser.NavigateRightAsync();
                break;

            case ControllerAction.NavigateLeft:
                _audioService.PlayNavigate();
                if (atHome) await _homeMenu.NavigateLeftAsync();
                else await _gameBrowser.NavigateLeftAsync();
                break;

            case ControllerAction.Select:
                if (atHome) await _homeMenu.ConfirmAsync();
                else await _gameBrowser.ConfirmAsync();
                break;

            case ControllerAction.Back:
                if (!atHome) await _gameBrowser.BackAsync();
                break;

            case ControllerAction.FilterOverlay:
                if (!atHome) OnOpenFilter();
                break;

            case ControllerAction.Secondary:
                if (!atHome) await _gameBrowser.ToggleFavoriteOnSelectedAsync();
                break;

            case ControllerAction.PlaylistNext:
                await _audioService.CyclePlaylistAsync(1, CurrentCategoryIdForPlaylistCycling(atHome));
                break;

            case ControllerAction.PlaylistPrevious:
                await _audioService.CyclePlaylistAsync(-1, CurrentCategoryIdForPlaylistCycling(atHome));
                break;

            case ControllerAction.TrackNext:
                _audioService.SkipToNextTrack();
                break;

            case ControllerAction.TrackPrevious:
                _audioService.SkipToPreviousTrack();
                break;

            // CategoryLeft/CategoryRight (LB/RB) intentionally have no case here —
            // they're free outside Settings for a future function. They still work
            // for sub-tab switching inside Settings (see the IsSettingsOpen branch
            // above), which is unaffected by this.

            case ControllerAction.Start:
                OnOpenSettings();
                break;
        }
    }
}
