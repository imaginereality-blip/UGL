using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UGL.App.ViewModels.Config;

namespace UGL.App.ViewModels;

/// <summary>A single row in the unified Settings menu — either a tab, or the Quit action.</summary>
public sealed record SettingsMenuItem(string Label, ConfigEditorViewModel.Tab? Tab, bool IsQuit = false);

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
    public VirtualKeyboardViewModel     VirtualKeyboard { get; }

    public enum Tab { Categories, Games, Systems, Audio, Theme, CardHighlight, Paths, Peripherals, Hooks }

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

    /// <summary>
    /// Rows for the unified settings menu (sidebar list). Selecting a tab row switches
    /// content immediately, same as the old tab buttons did on click. The Quit row is
    /// deliberately NOT auto-activated on selection — only an explicit confirm (Select on
    /// a controller, or clicking it) exits the app, via ConfirmQuit(). Just highlighting it
    /// while browsing the menu must never risk quitting by accident.
    /// </summary>
    public ObservableCollection<SettingsMenuItem> MenuItems { get; } =
    [
        new("🗂 Categories",         Tab.Categories),
        new("🎮 Games",             Tab.Games),
        new("🖥 Systems",            Tab.Systems),
        new("🎵 Audio",              Tab.Audio),
        new("🎨 Theme",              Tab.Theme),
        new("🌟 Card Highlight",    Tab.CardHighlight),
        new("📁 Paths",              Tab.Paths),
        new("🕹 Controllers",        Tab.Peripherals),
        new("🔌 Output Hooks",       Tab.Hooks),
        new("⏻ Quit UGL",           null, IsQuit: true),
    ];

    [ObservableProperty] private SettingsMenuItem? _selectedMenuItem;

    /// <summary>
    /// Whether controller input is currently focused on a tab's content (true) or the
    /// sidebar menu (false). Right/Select on a tab row enters content; Back exits it one
    /// level at a time back to the sidebar before closing Settings entirely — standard
    /// nested-menu behavior.
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
        }
    }

    public void SwitchSubTabRight()
    {
        switch (ActiveTab)
        {
            case Tab.Audio:   Audio.SwitchSubTabRight();   break;
            case Tab.Systems: Systems.SwitchSubTabRight(); break;
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
        VirtualKeyboardViewModel virtualKeyboard)
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
        VirtualKeyboard = virtualKeyboard;

        SelectedMenuItem = MenuItems[0];
    }

    partial void OnSelectedMenuItemChanged(SettingsMenuItem? value)
    {
        // Only tab rows auto-switch content on selection. Quit requires an explicit
        // confirm (ConfirmQuit) — see the class-level remarks on MenuItems above.
        if (value is { IsQuit: false, Tab: { } tab })
            SelectTab(tab);
    }

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
        idx = (idx - 1 + MenuItems.Count) % MenuItems.Count;
        SelectedMenuItem = MenuItems[idx];
    }

    public void NavigateMenuDown()
    {
        if (MenuItems.Count == 0) return;
        int idx = SelectedMenuItem is null ? 0 : MenuItems.IndexOf(SelectedMenuItem);
        idx = (idx + 1) % MenuItems.Count;
        SelectedMenuItem = MenuItems[idx];
    }

    /// <summary>
    /// A/Select while the menu has the highlight. Quit requires this explicit confirm.
    /// For a tab row, this enters that tab's content — same as pressing Right.
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

    /// <summary>Moves controller focus from the sidebar into the current tab's content.</summary>
    public void EnterContent()
    {
        if (SelectedMenuItem is { IsQuit: false })
            IsContentFocused = true;
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
        Tab.Games => Games.TryExitCategoryOptions() || Games.TryExitBiosOverrides(),
        Tab.Audio => Audio.TryExitTrackList(),
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
                break;
            case Tab.Systems:
                Systems.IsSystemEditorOpen = false;
                Systems.IsEmulatorEditorOpen = false;
                Systems.IsBiosListFocused = false;
                break;
            case Tab.Audio:
                Audio.IsMusicListFocused = true;
                Audio.IsTrackListFocused = false;
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
        }
    }

    /// <summary>Temporary diagnostic helper — reports the active tab's key list/selection state.</summary>
    public string GetContentDebugState() => ActiveTab switch
    {
        Tab.Categories    => $"IsCategoryListFocused={Categories.IsCategoryListFocused} SelectedCategory={Categories.SelectedCategory?.Label ?? "null"} CategoryFocusIndex={Categories.CategoryFocusIndex} KeyboardIsOpen={VirtualKeyboard.IsOpen}",
        Tab.Games         => $"FilteredGames.Count={Games.FilteredGames.Count} SelectedGame={Games.SelectedGame?.Title ?? "null"} IsEditorOpen={Games.IsEditorOpen} EditorFocusIndex={Games.EditorFocusIndex} KeyboardIsOpen={VirtualKeyboard.IsOpen}",
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
    }

    public void SelectTab(Tab tab)
    {
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
        IsContentFocused          = false;

        // Keep the menu list's highlight in sync when the tab changes via a route
        // other than selecting it directly in the list.
        var matching = MenuItems.FirstOrDefault(m => m.Tab == tab);
        if (matching is not null && !ReferenceEquals(SelectedMenuItem, matching))
            SelectedMenuItem = matching;
    }
}
