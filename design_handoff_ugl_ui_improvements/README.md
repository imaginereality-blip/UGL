# Handoff: UGL UI Cleanup & Settings Reorganization

## Overview
Fifteen recommendations to make UltimateGameLauncher's Avalonia UI read as cleaner
and more professional, without touching its controller-first architecture. Covers
the shell (cards, hint bar, toasts) and a deep-dive on the Settings screen
(field density, the Theme tab's broken picker, sidebar organization).

## About the design files
`UGL UI Recommendations.dc.html` (included) is an **HTML design reference**, not
code to port. UGL is a native Avalonia/.NET 9 app — every change below must be
implemented as AXAML + C# in the existing `UGL.App`/`UGL.Themes` projects, using
the app's own `DynamicResource` theme-key system, not CSS or web markup. The HTML
file exists only so you can see each before/after side by side; treat its colors,
spacing and copy as the target values to reproduce in XAML.

## Fidelity
High-fidelity on values (exact hex colors, corner radii, spacing), but the
mockups are schematic representations of Avalonia panels, not final controls —
implement each using real Avalonia `Border`/`ListBox`/`Style` selectors per the
codebase's established `cfgField`/`ThemeKeys` patterns, not by copying markup.

## Constraints (already respected in every recommendation)
- Pure AXAML + `{DynamicResource UGL.*}` theme keys — no CSS, no new dependencies
- Fixed 1920×1080 full-screen, no window chrome
- Card faces stay art-only — no text/gradient added to the card surface (§6.1 of the SDS)
- No hover states — every interaction is D-pad/analog-stick/button driven
- Card/list selection is driven from code-behind and ViewModel bools, not Avalonia's native focus system

## Recommendations

### Shell-wide
1. **Theme tokens for overlays** (Quick win) — `ThemeKeys.cs`, `AvaloniaThemeService.cs`, `ConfigEditorView`/`FilterOverlayView`/`MainWindow.axaml`. Replace the ~8 hand-rolled translucent grays (`#22FFFFFF`, `#33FFFFFF`, `#44FFFFFF`, `#66FFFFFF`, `#88FFFFFF`, `#CC000000`, `#EE0D0D1A`, `#F00D0D1A`) with named tokens: `Divider`, `ScrimHeavy`, `PanelBackground` (alongside the existing, currently-unused `ThemeKeys.OverlayBg`).
2. **Wire `CardSpacing`; soften selection into a glow** (Quick win) — `CategoryCard.axaml(.cs)`, `GameCard.axaml(.cs)`. Bind the dead `ThemeKeys.CardSpacing` token to the card `Margin` (currently hardcoded `Margin="2"`). Add a `DropShadowEffect` in the highlight color alongside the existing border for a glow rather than a hard line — keep the border, keep "no scale/no card-face content."
3. **Button-hint chips instead of run-on strings** (Medium) — new `Views/Controls/ButtonHint.axaml(.cs)` UserControl (round glyph badge + label); replace the single long `TextBlock` hint strings in `HomeMenuView`, `GameBrowserView`, `ConfigEditorView`, `FilterOverlayView`.
4. **Settings sidebar: left-accent bar instead of full fill** (Quick win) — `ConfigEditorView.axaml`. Replace `ListBoxItem:selected { Background: AccentColor }` with a low-opacity accent tint + 3px left `Border`.
5. **Toasts as dark surfaces with an accent stripe** (Quick win) — `MainWindow.axaml`. Replace the solid `#CCFF2222`/`#CC2266CC` fills with `SurfaceColor` background + colored left stripe + status glyph.
6. **Segoe Fluent Icons instead of emoji** (Quick win) — `ConfigEditorView.axaml`, `GameCard.axaml`, and every Config view listed below. Set `FontFamily="Segoe Fluent Icons"` (ships with Windows 10/11, zero new dependency) on the `⚙ ✕ ★ 🎵 🔊 🖼 🎮 🗂 🖥 📁 🕹 🔌 🔄` glyphs.

### Settings screen — field-level
7. **Group the Games editor into labeled sections** (Medium) — `GamesConfigView.axaml`. Break the ~15-field flat `StackPanel` into captioned groups (Identity / Launch overrides / Display & Input) with hairline dividers.
8. **Pull controller-hint text out of field labels** (Quick win) — `GamesConfigView.axaml` and other `Config/*.axaml`. Labels like "System * (controller: Left/Right)" shrink to just the name/requirement; the control-scheme text moves to the shared hint bar, driven by whichever field currently has focus.
9. **Fix the Theme tab's duplicate, non-functional picker** (Medium) — `ThemeConfigView.axaml(.cs)`. The swatch cards' three color dots are hardcoded literals identical on every card; bind them to each theme's own `AccentColor`/`BackgroundColor`/`SurfaceColor`, and remove the separate `ListBox` beneath — put the `SelectedItem` binding on the card list itself.
10. **`Success`/`Danger` tokens; reuse `quitRow` on Delete buttons** (Quick win) — `ThemeKeys.cs`, `AvaloniaThemeService.cs`, `ConfigEditorView.axaml` + `GamesConfigView`/`CategoriesConfigView`/`SystemsConfigView`. Replace hardcoded `#FFFF4444`/`#FF44FF88` validation/status colors with theme tokens; apply the existing `quitRow` red style (currently only used on Settings' Quit row) to every tab's Delete button.
11. **Collapse the Paths tab's 5 repeated blocks into a table** (Medium) — `PathsConfigView.axaml(.cs)`. Media/ROMs/Emulators/Addons/Logs Root currently repeat an identical tall field block; a compact label/path/Browse table fits all five where two currently sit.
12. **Real card-preview in the Card Highlight tab** (Quick win) — `CardHighlightConfigView.axaml`. Replace the plain "Preview" text box with a placeholder card thumbnail (reuse `CategoryCard`'s big-letter placeholder) so the border/glow renders exactly as it will in play.

### Settings screen — navigation & organization
13. **Collapse the sidebar on explicit A-enter, not on arrow-move** (Quick win — the state plumbing already exists) — `ConfigEditorView.axaml` only. `ConfigEditorViewModel.IsContentFocused`/`EnterContent()`/`ExitContent()` are already implemented; the AXAML sidebar column width just needs to bind to `IsContentFocused` (240px when false, collapsed when true) to actually give content the full width once a tab is entered. Arrow-key live-preview browsing of the sidebar is unaffected.
14. **Group the 11-row sidebar into labeled clusters** (Quick win) — `ConfigEditorViewModel.cs` (`MenuItems`), `ConfigEditorView.axaml`. Add a non-selectable header-row variant of `SettingsMenuItem` (skipped by `NavigateMenuUp/Down`) to split the flat list into Content (Categories/Games/Systems), Appearance (Theme/Card Highlight), and System (Audio/Paths/Controllers/Output Hooks/Updates/Scraper).
15. **Fix the Systems tab's unstyled sub-tab buttons** (Quick win) — `SystemsConfigView.axaml`. Its `SystemsTabBtn`/`EmulatorsTabBtn` are plain `Button`s with no focus wrapper — wrap them in `Classes="cfgField" Classes.fieldFocused="…"` the same way Games' (Settings/Art) and Audio's (Music/System Sounds) sub-tab buttons already are, so LB/RB switching gives visible feedback here too.

## Design tokens to add
`ThemeKeys.cs` / `AvaloniaThemeService.BuildResourceDictionary`:
- `Divider` — replaces `#22FFFFFF`/`#33FFFFFF`/`#44FFFFFF`/`#66FFFFFF` border literals
- `ScrimHeavy` — replaces `#CC000000`/`#EE0D0D1A`/`#F00D0D1A` backdrop literals
- `PanelBackground` — replaces `#110D0D1A` settings-panel literals
- `Success` — replaces `#FF44FF88` status-message literal
- `Danger` — replaces `#FFFF4444` validation-error literal (and doubles as the Delete-button color, already matching `quitRow`'s `#FFFF5C5C`)

(`ThemeKeys.OverlayBg` already exists and is currently unreferenced — wire it in alongside these rather than adding a duplicate.)

## Files
- `UGL UI Recommendations.dc.html` — the full before/after visual reference for all 15 items, plus a rollout-order table
- `screenshots/` — full-page captures of that document, in reading order (00-overview-hero through 13-tail-rollout-table)
