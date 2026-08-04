using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using UGL.App.ViewModels.Config;

namespace UGL.App.ViewModels;

/// <summary>
/// A single row in the unified Settings menu — a tab, the Quit action, or a
/// non-selectable group header (Content / Appearance / System) used purely to
/// visually cluster the rows beneath it. Header rows are skipped by
/// NavigateMenuUp/Down and disabled in the ListBox so they can't be highlighted
/// or clicked.
/// </summary>
public sealed record SettingsMenuItem(
    string Label,
    ConfigEditorViewModel.Tab? Tab,
    bool IsQuit = false,
    bool IsHeader = false,
    string Icon = "");

public sealed partial class ConfigEditorViewModel : ObservableObject
{
    public CategoriesConfigViewModel    Categories    { get; }
    public GamesConfigViewModel         Games         { get; }
    public SystemsConfigViewModel       Systems       { get; }
    public AudioConfigViewModel         Audio         { get; }
    public ThemeConfigViewModel         Theme         { get; }
    public CardHighlightConfigViewModel CardHighlight { get; }
    public PathsConfigViewModel         Paths         { get; }
    public PeripheralConfigViewModel    Peripherals   { get; }
    public HookConfigViewModel          Hooks         { get; }
    public UpdateConfigViewModel        Updates       { get; }
    public ScraperConfigViewModel       Scraper       { get; }
    public VirtualKeyboardViewModel     VirtualKeyboard { get; }
    public ConfirmDialogViewModel       ConfirmDialog { get; }

    private readonly ILogger<ConfigEditorViewModel> _logger;

    public enum Tab { Categories, Games, Systems, Audio, Theme, CardHighlight, Paths, Peripherals, Hooks, Updates, Scraper }

    [ObservableProperty] private Tab  _activeTab = Tab.Categories;
    [ObservableProperty] private bool _isCategoriesTabActive = true;
    [ObservableProperty] private bool _isGamesTabActive;
    [ObservableProperty] private bool _isSystemsTabActive;
    [ObservableProperty] private bool _isAudioTabActive;
    [ObservableProperty] private bool _isThemeTabActive;
    [ObservableProperty] private bool _isCardHighlightTabActive;
    [ObservableProperty] private bool _isPathsTabActive;
    [ObservableProperty] private bool _isPeripheralsTabActive;
    [ObservableProperty] private bool _isHooksTabActive;
    [ObservableProperty] private bool _isUpdatesTabActive;
    [ObservableProperty] private bool _isScraperTabActive;

    /// <summary>
    /// Rows for the unified settings menu (sidebar list). Selecting a tab row switches
    /// content immediately, same as the old tab buttons did on click. The Quit row is
    /// deliberately NOT auto-activated on selection — only an explicit confirm (Select on
    /// a controller, or clicking it) exits the app, via ConfirmQuit(). Just highlighting it
    /// while browsing the menu must never risk quitting by accident.
    /// </summary>
    public ObservableCollection<SettingsMenuItem> MenuItems { get; } =
    [
        new("Content",              null, IsHeader: true),
        new("Categories",           Tab.Categories,     Icon: ""),
        new("Games",                Tab.Games,          Icon: "🎮"),
        new("Systems",              Tab.Systems,        Icon: "🖥"),

        new("Appearance",           null, IsHeader: true),
        new("Theme",                Tab.Theme,          Icon: "🎨"),
        new("Card Highlight",       Tab.CardHighlight,  Icon: ""),

        new("System",               null, IsHeader: true),
        new("Audio",                Tab.Audio,          Icon: "🎵"),
        new("Paths",                Tab.Paths,          Icon: ""),
        new("Controllers",          Tab.Peripherals,    Icon: "🕹"),
        new("Output Hooks",         Tab.Hooks,          Icon: "🔌"),
        new("Updates",              Tab.Updates,        Icon: ""),
        new("Scraper",              Tab.Scraper,        Icon: "🖼"),

        new("Quit UGL",             null, IsQuit: true, Icon: ""),
    ];

    [ObservableProperty] private SettingsMenuItem? _selectedMenuItem;

    /// <summary>
    /// Whether controller input is currently focused on a tab's content (true) or the
    /// sidebar menu (false). Only an explicit Select on a tab row enters content
    /// (mouse click or gamepad Confirm — see EnterContent) — NOT the stick's Right
    /// direction, which used to also enter and caused reported "stick drift". Back
    /// exits it one level at a time back to the sidebar before closing Settings
    /// entirely — standard nested-menu behavior.
    /// </summary>
    [ObservableProperty] private bool _isContentFocused;

    /// <summary>Raised only when Quit is explicitly confirmed — see ConfirmQuit().</summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Raised when the right stick should scroll whatever content is currently visible.
    /// direction is -1 (up) or +1 (down). Handled in the View's code-behind since finding
    /// "whichever ScrollViewer is currently on screen" is inherently a visual-tree concern,
    /// not something the ViewModel layer should reach into.
    /// </summary>
    public event Action<int>? ScrollRequested;

    public void ScrollContent(int direction) => ScrollRequested?.Invoke(direction);

    /// <summary>
    /// Dedicated LB/RB sub-tab switching (Music/Sounds within Audio, Systems/Emulators
    /// within Systems). Tabs with no sub-tabs simply no-op. Kept separate from
    /// Left/Right, which within a tab's content is overloaded to adjust the currently
    /// focused value — LB/RB gives a switch that's always available regardless of
    /// what's focused, matching what the Settings hint bar already claimed (but never
    /// actually implemented) about LB/RB navigating.
    /// </summary>
    public void SwitchSubTabLeft()
    {
        switch (ActiveTab)
        {
            case Tab.Audio:   Audio.SwitchSubTabLeft();   break;
            case Tab.Systems: Systems.SwitchSubTabLeft(); break;
            case Tab.Games:   Games.SwitchSubTabLeft();   break;
        }
    }

    public void SwitchSubTabRight()
    {
        switch (ActiveTab)
        {
            case Tab.Audio:   Audio.SwitchSubTabRight();   break;
            case Tab.Systems: Systems.SwitchSubTabRight(); break;
            case Tab.Games:   Games.SwitchSubTabRight();   break;
        }
    }

    public ConfigEditorViewModel(
        CategoriesConfigViewModel categories,
        GamesConfigViewModel games,
        SystemsConfigViewModel systems,
        AudioConfigViewModel audio,
        ThemeConfigViewModel theme,
        CardHighlightConfigViewModel cardHighlight,
        PathsConfigViewModel paths,
        PeripheralConfigViewModel peripherals,
        HookConfigViewModel hooks,
        UpdateConfigViewModel updates,
        ScraperConfigViewModel scraper,
        VirtualKeyboardViewModel virtualKeyboard,
        ConfirmDialogViewModel confirmDialog,
        ILogger<ConfigEditorViewModel> logger)
    {
        Categories    = categories;
        Games         = games;
        Systems       = systems;
        Audio         = audio;
        Theme         = theme;
        CardHighlight = cardHighlight;
        Paths         = paths;
        Peripherals   = peripherals;
        Hooks         = hooks;
        Updates       = updates;
        Scraper       = scraper;
        VirtualKeyboard = virtualKeyboard;
        ConfirmDialog = confirmDialog;
        _logger       = logger;

        SelectedMenuItem = MenuItems.FirstOrDefault(m => !m.IsHeader);
    }

    // Highlighting a row (via NavigateMenuUp/Down while browsing the sidebar with a
    // stick/D-pad) intentionally does NOT switch the visible content panel — only an
    // explicit confirm does, via EnterContent(). Without this, scrolling the sidebar
    // flickers through every tab's content on the way to the one you actually want.

    /// <summary>
    /// Called when Quit is explicitly activated (controller Select, or a click) while
    /// highlighted — not merely when it becomes the highlighted row.
    /// </summary>
    public void ConfirmQuit() => QuitRequested?.Invoke();

    // ── Direct menu navigation (no Avalonia focus system involved) ─────────
    // Mirrors FilterOverlayViewModel's proven NavigateUp/Down/Confirm pattern:
    // MainWindowViewModel calls these directly when Settings is open, rather than
    // routing through the View's real keyboard-focus machinery, which proved
    // unreliable to drive synthetically. SelectedMenuItem is a normal bound
    // property, so the ListBox's visual highlight follows it regardless of
    // literal input focus.

    public void NavigateMenuUp()
    {
        if (MenuItems.Count == 0) return;
        int idx = SelectedMenuItem is null ? 0 : MenuItems.IndexOf(SelectedMenuItem);
        do { idx = (idx - 1 + MenuItems.Count) % MenuItems.Count; }
        while (MenuItems[idx].IsHeader);
        SelectedMenuItem = MenuItems[idx];
    }

    public void NavigateMenuDown()
    {
        if (MenuItems.Count == 0) return;
        int idx = SelectedMenuItem is null ? 0 : MenuItems.IndexOf(SelectedMenuItem);
        do { idx = (idx + 1) % MenuItems.Count; }
        while (MenuItems[idx].IsHeader);
        SelectedMenuItem = MenuItems[idx];
    }

    /// <summary>
    /// A/Select while the menu has the highlight. Quit requires this explicit confirm.
    /// For a tab row, this enters that tab's content — the only way in; Right no
    /// longer does this (see IsContentFocused remarks).
    /// </summary>
    public void ConfirmMenuSelection()
    {
        if (SelectedMenuItem?.IsQuit == true)
        {
            ConfirmQuit();
            return;
        }

        EnterContent();
    }

    /// <summary>
    /// Moves controller focus from the sidebar into the current tab's content. This is
    /// the only place a highlighted row actually switches the visible content panel —
    /// see the remarks on OnSelectedMenuItemChanged above.
    /// </summary>
    public void EnterContent()
    {
        if (SelectedMenuItem is { IsQuit: false, Tab: { } tab })
        {
            _logger.LogDebug("EnterContent: entering {Tab} (was ActiveTab={ActiveTab}, IsContentFocused={WasFocused})",
                tab, ActiveTab, IsContentFocused);
            SelectTab(tab);
            IsContentFocused = true;
        }
    }

    /// <summary>
    /// Gives the active tab a chance to close just its own nested sub-mode (a checkbox
    /// grid, a track sub-list, etc.) without exiting the tab's content entirely — real
    /// multi-level Back, so in-progress edits in a still-open editor aren't lost just
    /// because you backed out of a nested list. Returns true if something was closed;
    /// the caller should only fall through to ExitContent() when this returns false.
    /// </summary>
    /// <summary>
    /// Direct "add new" shortcut (X while a tab's own list — not an editor — has
    /// focus). Currently only Categories supports this; other tabs no-op.
    /// </summary>
    public void QuickAddInContent()
    {
        if (!IsContentFocused) return;
        if (ActiveTab == Tab.Categories && Categories.IsCategoryListFocused)
            Categories.QuickAddNew();
    }

    public bool TryHandleContentBack() => ActiveTab switch
    {
        Tab.Games => Games.TryExitCategoryOptions() || Games.TryExitBiosOverrides() || Games.TryExitDisabledDeviceTypes() || Games.TryExitPlayerAssignments(),
        Tab.Audio => Audio.TryExitSubMode(),
        Tab.Hooks => Hooks.TryExitOverrides(),
        Tab.Systems => Systems.TryExitBiosList(),
        _ => false,
    };

    /// <summary>Moves controller focus back from a tab's content to the sidebar.</summary>
    public void ExitContent()
    {
        // Closing an editor here (rather than saving) matches Back's usual meaning
        // elsewhere in Settings — discard/dismiss rather than commit. Without this,
        // the editor stays silently open in the background and reappears exactly as
        // you left it next time you return to the tab, which reads as being "stuck".
        switch (ActiveTab)
        {
            case Tab.Categories:
                Categories.IsCategoryListFocused = true;
                break;
            case Tab.Games:
                Games.IsEditorOpen = false;
                Games.IsCategoryOptionsFocused = false;
                Games.IsBrowsingBiosOverrides = false;
                Games.IsBrowsingDisabledDeviceTypes = false;
                break;
            case Tab.Systems:
                Systems.IsSystemEditorOpen = false;
                Systems.IsEmulatorEditorOpen = false;
                Systems.IsBiosListFocused = false;
                break;
            case Tab.Audio:
                Audio.IsCategoriesSubModeFocused = false;
                Audio.IsAvailableTracksSubModeFocused = false;
                Audio.IsAssignedTracksSubModeFocused = false;
                break;
            case Tab.Hooks:
                Hooks.IsOverridesFocused = false;
                break;
        }

        IsContentFocused = false;
    }

    // ── Content navigation dispatch ──────────────────────────────────────────
    // Forwards to whichever tab's ViewModel is active. Each tab's Navigate/Confirm
    // methods cover list browsing and primary actions; free-text form fields still
    // need keyboard/mouse, since no on-screen keyboard exists yet.

    public void NavigateContentUp()
    {
        switch (ActiveTab)
        {
            case Tab.Categories:    Categories.NavigateUp();    break;
            case Tab.Games:         Games.NavigateUp();         break;
            case Tab.Systems:       Systems.NavigateUp();       break;
            case Tab.Audio:         Audio.NavigateUp();         break;
            case Tab.Theme:         Theme.NavigateUp();         break;
            case Tab.CardHighlight: CardHighlight.NavigateUp(); break;
            case Tab.Paths:         Paths.NavigateUp();         break;
            case Tab.Peripherals:   Peripherals.NavigateUp();   break;
            case Tab.Hooks:         Hooks.NavigateUp();         break;
            case Tab.Updates:       Updates.NavigateUp();       break;
            case Tab.Scraper:       Scraper.NavigateUp();       break;
        }
    }

    public void NavigateContentDown()
    {
        switch (ActiveTab)
        {
            case Tab.Categories:    Categories.NavigateDown();    break;
            case Tab.Games:         Games.NavigateDown();         break;
            case Tab.Systems:       Systems.NavigateDown();       break;
            case Tab.Audio:         Audio.NavigateDown();         break;
            case Tab.Theme:         Theme.NavigateDown();         break;
            case Tab.CardHighlight: CardHighlight.NavigateDown(); break;
            case Tab.Paths:         Paths.NavigateDown();         break;
            case Tab.Peripherals:   Peripherals.NavigateDown();   break;
            case Tab.Hooks:         Hooks.NavigateDown();         break;
            case Tab.Updates:       Updates.NavigateDown();       break;
            case Tab.Scraper:       Scraper.NavigateDown();       break;
        }
    }

    public void NavigateContentLeft()
    {
        switch (ActiveTab)
        {
            case Tab.Categories:    Categories.NavigateLeft();    break;
            case Tab.Games:         Games.NavigateLeft();         break;
            case Tab.Systems:       Systems.NavigateLeft();       break;
            case Tab.Audio:         Audio.NavigateLeft();         break;
            case Tab.Theme:         Theme.NavigateLeft();         break;
            case Tab.CardHighlight: CardHighlight.NavigateLeft(); break;
            case Tab.Paths:         Paths.NavigateLeft();         break;
            case Tab.Peripherals:   Peripherals.NavigateLeft();   break;
            case Tab.Hooks:         Hooks.NavigateLeft();         break;
            case Tab.Scraper:       Scraper.NavigateLeft();       break;
        }
    }

    public void NavigateContentRight()
    {
        switch (ActiveTab)
        {
            case Tab.Categories:    Categories.NavigateRight();    break;
            case Tab.Games:         Games.NavigateRight();         break;
            case Tab.Systems:       Systems.NavigateRight();       break;
            case Tab.Audio:         Audio.NavigateRight();         break;
            case Tab.Theme:         Theme.NavigateRight();         break;
            case Tab.CardHighlight: CardHighlight.NavigateRight(); break;
            case Tab.Paths:         Paths.NavigateRight();         break;
            case Tab.Peripherals:   Peripherals.NavigateRight();   break;
            case Tab.Hooks:         Hooks.NavigateRight();         break;
            case Tab.Scraper:       Scraper.NavigateRight();       break;
        }
    }

    public async Task ConfirmContentAsync()
    {
        switch (ActiveTab)
        {
            case Tab.Categories:    await Categories.ConfirmAsync();    break;
            case Tab.Games:         await Games.ConfirmAsync();         break;
            case Tab.Systems:       await Systems.ConfirmAsync();       break;
            case Tab.Audio:         await Audio.ConfirmAsync();         break;
            case Tab.Theme:         await Theme.ConfirmAsync();         break;
            case Tab.CardHighlight: await CardHighlight.ConfirmAsync(); break;
            case Tab.Paths:         await Paths.ConfirmAsync();         break;
            case Tab.Peripherals:   await Peripherals.ConfirmAsync();   break;
            case Tab.Hooks:         await Hooks.ConfirmAsync();         break;
            case Tab.Updates:       await Updates.ConfirmAsync();       break;
            case Tab.Scraper:       await Scraper.ConfirmAsync();       break;
        }
    }

    /// <summary>Temporary diagnostic helper — reports the active tab's key list/selection state.</summary>
    public string GetContentDebugState() => ActiveTab switch
    {
        Tab.Categories    => $"IsCategoryListFocused={Categories.IsCategoryListFocused} SelectedCategory={Categories.SelectedCategory?.Label ?? "null"} CategoryFocusIndex={Categories.CategoryFocusIndex} KeyboardIsOpen={VirtualKeyboard.IsOpen}",
        Tab.Games         => $"FilteredGames.Count={Games.FilteredGames.Count} SelectedGame={Games.SelectedGame?.Title ?? "null"} IsEditorOpen={Games.IsEditorOpen} IsArtTabActive={Games.IsArtTabActive} SettingsFocusIndex={Games.SettingsFocusIndex} ArtFocusIndex={Games.ArtFocusIndex} KeyboardIsOpen={VirtualKeyboard.IsOpen}",
        Tab.Systems       => $"IsSystemsTabActive={Systems.IsSystemsTabActive} SelectedSystem={Systems.SelectedSystem?.Name ?? "null"} SelectedEmulator={Systems.SelectedEmulator?.Name ?? "null"} IsSystemEditorOpen={Systems.IsSystemEditorOpen} SystemEditorFocusIndex={Systems.SystemEditorFocusIndex} IsEmulatorEditorOpen={Systems.IsEmulatorEditorOpen} EmulatorEditorFocusIndex={Systems.EmulatorEditorFocusIndex} KeyboardIsOpen={VirtualKeyboard.IsOpen}",
        Tab.Audio         => $"IsMusicTabActive={Audio.IsMusicTabActive} SelectedPlaylist={Audio.SelectedPlaylist?.Name ?? "null"} SoundsFocusIndex={Audio.SoundsFocusIndex}",
        Tab.Theme         => $"SelectedTheme={Theme.SelectedTheme?.Name ?? "null"}",
        Tab.CardHighlight => $"FocusIndex={CardHighlight.FocusIndex} Color={CardHighlight.CustomHex} Thickness={CardHighlight.Thickness} Intensity={CardHighlight.Intensity} Pulsing={CardHighlight.IsPulsing}",
        Tab.Paths         => $"FocusIndex={Paths.FocusIndex}",
        Tab.Peripherals   => $"Devices.Count={Peripherals.Devices.Count} SelectedDevice={Peripherals.SelectedDevice?.FriendlyName ?? "null"}",
        Tab.Hooks         => $"FocusIndex={Hooks.FocusIndex} Enabled={Hooks.EnabledGlobally} Tool={Hooks.ToolType}",
        _ => "unknown",
    };

    public async Task InitializeAsync()
    {
        await Categories.InitializeAsync();
        await Games.InitializeAsync();
        await Systems.InitializeAsync();
        await Audio.InitializeAsync();
        await Theme.InitializeAsync();
        await CardHighlight.InitializeAsync();
        await Paths.InitializeAsync();
        await Peripherals.InitializeAsync();
        await Hooks.InitializeAsync();
        await Scraper.InitializeAsync();
    }

    public void SelectTab(Tab tab)
    {
        // Diagnostic for the reported "stick drift switches tabs" issue — code
        // inspection found no remaining path that should call this outside
        // EnterContent(), so if it recurs, this pinpoints the real caller instead of
        // further guessing. Safe to remove once that's confirmed fixed.
        _logger.LogDebug("SelectTab: {Tab}. Caller stack: {Stack}", tab,
            new System.Diagnostics.StackTrace(1, false).ToString().Replace(Environment.NewLine, " | "));

        ActiveTab                 = tab;
        IsCategoriesTabActive     = tab == Tab.Categories;
        IsGamesTabActive          = tab == Tab.Games;
        IsSystemsTabActive        = tab == Tab.Systems;
        IsAudioTabActive          = tab == Tab.Audio;
        IsThemeTabActive          = tab == Tab.Theme;
        IsCardHighlightTabActive  = tab == Tab.CardHighlight;
        IsPathsTabActive          = tab == Tab.Paths;
        IsPeripheralsTabActive    = tab == Tab.Peripherals;
        IsHooksTabActive          = tab == Tab.Hooks;
        IsUpdatesTabActive        = tab == Tab.Updates;
        IsScraperTabActive        = tab == Tab.Scraper;
        IsContentFocused          = false;

        // Keep the menu list's highlight in sync when the tab changes via a route
        // other than selecting it directly in the list.
        var matching = MenuItems.FirstOrDefault(m => m.Tab == tab);
        if (matching is not null && !ReferenceEquals(SelectedMenuItem, matching))
            SelectedMenuItem = matching;
    }
}
