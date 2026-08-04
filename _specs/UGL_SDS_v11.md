# UltimateGameLauncher (UGL) — Software Design Specification

> **Version:** 13.0.0
> **Target Framework:** .NET 9.0 (net9.0-windows)
> **UI Framework:** Avalonia UI 11.2.2 (MVVM via CommunityToolkit.Mvvm)

**Version History**
- **13.0.0** — Generator #1 ("Resize Cover to Card") reworked into "🧹 Clean Cover for
  Card" (§9.7), replacing a plain crop/resize with a 4-step pipeline: remove logo/text
  via ComfyUI masked inpainting, resize (not crop) to card size, upscale/increase
  detail, composite the real logo on top. Three rejected intermediate approaches along
  the way (documented in full in §9.7, since each looked plausible until tested):
  whole-image img2img at raised denoise (either left text intact or distorted the
  whole picture), pre-squashing the cover into the card's tall aspect before cleanup
  (distorted the art on its own), and a fixed top-28%-band mask (worked for one test
  cover, didn't generalize). Landed on detection-driven masking: `LogoRegionDetector`
  (new, `UGL.App`) template-matches the game's own scraped logo asset against the
  cover via OpenCV (`OpenCvSharp4`, new dependency) to find the real logo wherever it
  sits; `TextRegionDetector` (new, `UGL.App`) optionally runs a local PP-OCRv3 "DB"
  text-detection ONNX model through OpenCV's DNN module to catch other stray badges —
  hand-rolled against the model's reference pre/post-processing since OpenCvSharp
  doesn't expose OpenCV's own high-level text-detection wrapper class, not yet visually
  verified. A `Windows.Media.Ocr` attempt at the same problem was abandoned for an
  environment reason (missing Windows App SDK build tooling for the version-qualified
  TFM it requires), not a design one. Separately: fixed a real, pre-existing, unrelated
  bug found while investigating where this cycle's generated art was actually being
  saved — `AppFolderScaffolder` (and its README, and a `PathsConfigView` string) had
  always documented/created a nested `media\games\covers\` layout that the actual
  save/load code never implemented, leaving those folders permanently empty while real
  art silently used the flat `media\covers\` form the code always actually used; §5 and
  the scaffolder now agree on the flat form. Two more real bugs found once the
  detection pipeline was actually live-tested: `BuildRegionsMask`'s blur step could
  silently collapse small mask regions to near-empty (a non-area-averaged downscale),
  and the only installed checkpoint (a general anime-style SD1.5 model) hallucinated
  garbled pseudo-text instead of cleanly erasing it. Fixed the mask-blur bug directly;
  fixed the checkpoint problem by switching the cleanup workflow to **Flux.1 Fill
  Dev** (a model purpose-built for mask-conditioned inpainting), rebuilding the node
  graph around `UNETLoader`/`DualCLIPLoader`/`VAELoader`/`DifferentialDiffusion`/
  `FluxGuidance`/`InpaintModelConditioning` — verified against ComfyUI's own official
  Flux Fill Dev example workflow, not guessed — with no C# changes needed beyond the
  default denoise value.
- **12.0.0** — AI/local Card Art generation system (§9.7): a new asset type, separate from
  Cover Art, produced by two generators — an instant local cover-resize ("Resize Cover to
  Card") and a ComfyUI-driven poster/collage generator (screenshots + cover → literal
  collage via low-denoise img2img → real scraped logo composited on top, never
  AI-regenerated text). Card resolution is read live from `CardDimensionInfo` (the actual
  on-screen card size) instead of a hardcoded constant. Screenshots are now a genuinely
  persisted multi-file asset (`GameMedia.ScreenshotPaths`), the first of its kind in the
  media system. Games tab restructured into Settings/Art sub-tabs (mirroring Audio's
  Music/Sounds split) with a full-width editor and a single large context-sensitive art
  preview, since the two-column single-screen layout became unusable once Card Art,
  screenshots, and generation controls were added. Settings-sidebar and Games-list
  gamepad gaps closed: mouse/gamepad menu-entry unified behind one explicit-confirm path
  (a stick-Right shortcut previously bypassed it entirely — the real cause of persistent
  "stick drift" reports), Add/Edit/Delete reachable via Left/Right + Confirm, a reusable
  Yes/No delete-confirmation overlay applied to all five destructive actions app-wide,
  and a dominant-axis gate on stick input so a diagonal push no longer fires both axes at
  once. Three real, independently-shipped bugs found and fixed this cycle, none related
  to the feature that surfaced them: an unquoted `{rom}` token breaking any ROM path with
  a space, `PeripheralRegistry.LoadAsync()` existing but never being called at startup,
  and TheGamesDB's `boxart` picker ignoring the API's own `front`/`back` `side` field.
- **11.0.0** — Full playlist system rework (§9.6): user-created playlists, independent Global/Category assignment, shared track library, drag-and-drop track assignment. Controller remap (§11g): Left Stick now drives all menu navigation, D-Pad repurposed for music control (Up/Down = playlist, Left/Right = track), LB/RB freed outside Settings. Per-game peripheral disable (§11h): silence a lightgun/wheel/etc. for games where it would interfere. A real, confirmed-in-production auto-play bug fixed via actual diagnostic logging rather than guesswork (§13) — root cause was a master-volume/per-playlist-volume conflict, not the playlist or track data at all. First public releases cut (v0.1.0, then v0.2.0), with the in-app update mechanism confirmed working end-to-end against a live GitHub release.
- **10.0.0** — Milestone 17 complete: portable-ZIP packaging (self-contained, single-file, win-x64), `scripts\build-release.ps1`, first real GitHub release (v0.1.0) published. Milestone 19: in-app update system (`UGL.Updates`, GitHub Releases-based, background + manual check, confirm-then-apply, self-relaunching batch-script apply mechanism). Milestone 20: new-user-friendly first-run experience (`START HERE.txt`, `media\README.txt`). Public GitHub repo established (GPL-3.0).
- **9.0.0** — Portable-path sweep across the entire app (drive-letter independence, §11c); BIOS files + bezel/overlay support including RetroArch's native overlay system (§11d); manual playlist switching with on-screen indicator (§9.4); Game Browser wraparound fix; folder scaffolding extended (bios\, bezels\); several real bugs found and fixed along the way (see §13 additions).
- **8.0.0** — Milestone 16 (Hook Integration: MameHooker/Hook of the Reaper process lifecycle, new `UGL.Hooks` project, §11b); built-in Favorites category (§6.2a); remaining controller-nav gaps closed (Audio Music tab, Games combo/checkbox fields, LB/RB sub-tab switching, Categories quick-add); Controllers tab renamed from "Peripheral Hooks."
- **7.0.0 and earlier** — not individually tracked here; see §14 Development Milestones for the full build history.
> **Status:** Active Development — see §14 Development Milestones for current state

---

## 1. Executive Summary

UltimateGameLauncher (UGL) is a professional, high-performance, controller-first Windows emulator frontend designed for arcade cabinets and home theater PCs (HTPCs). It delivers a premium, hardware-accelerated experience modeled after modern game console dashboards. Version 7.0.0 formalises full controller navigation across every Settings tab, an on-screen virtual keyboard for text entry without a physical keyboard, multi-category game assignment, and a fully configurable card selection highlight (color, intensity, border width, solid/pulsing style) shared between the Home Menu and Game Browser.

---

## 2. Project Vision & Design Philosophy

- **Controller-First UX:** Every interaction is optimized for XInput controllers. Keyboard and mouse are secondary fallbacks. As of Milestone 15, this now holds for Settings too — every tab, every field, and the on-screen keyboard are fully operable with a controller alone.
- **Graphical-First UI:** Menus are fully graphical. Text is minimal. Cards ARE the screen.
- **Full-Height Cards:** Five cards fill the entire screen height. No top bar, no floating panels.
- **Two-Level Navigation:** Home Menu (categories) → Game Browser (games). B always returns.
- **Zero-Lag Fluidity:** Locked 60 FPS. No UI stuttering during disk I/O or asset loading.
- **Complete Data Separation:** All layouts, metadata, and styles are data-driven via local JSON.
- **Modular Architecture:** Features isolated behind strict service interfaces. All services replaceable via DI.
- **Portable by Default:** All paths relative to the exe. Any path can be overridden to any drive or network share.

---

## 3. Technology Stack

| Component | Technology |
|---|---|
| Language | C# 12/13 |
| Runtime | .NET 9.0 (net9.0-windows) |
| UI Engine | Avalonia UI 11.2.2 |
| Graphics Backend | SkiaSharp (GPU accelerated) |
| MVVM | CommunityToolkit.Mvvm (source generators) |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |
| Hosting | Microsoft.Extensions.Hosting (IHost) |
| Input | XInput via P/Invoke (`xinput1_4.dll`) + Win32 RawInput (lightguns, wheels) |
| Image Cache | `Avalonia.Media.Imaging.Bitmap` — lock-free concurrent reads, LRU eviction |
| Audio | LibVLCSharp 3.9.0 + VideoLAN.LibVLC.Windows 3.0.21 |
| Video Preview | LibVLCSharp.Avalonia 3.9.0 (`VideoView` control) |
| IPC | Win32 `SendMessage`/`PostMessage`, Named Pipes, Registry/MMF mapping |
| Hooking | `mamehook.exe` or `HookOfTheReaper.exe` — light gun recoil/rumble + cabinet lighting, driven by the emulator's own MAME-standard output signals (§11b) |
| Logging | Microsoft.Extensions.Logging + Console + File (`logs\ugl.log`) |

---

## 4. Solution Layout

```
UGL.sln
├── src/
│   ├── UGL.App/          # Executable, Views, ViewModels, composition root
│   ├── UGL.Core/         # Interfaces, domain models (zero UGL dependencies)
│   ├── UGL.Data/         # JSON repositories
│   ├── UGL.Media/        # Image cache, video preview, audio service
│   ├── UGL.Input/        # XInput, RawInput multi-device
│   ├── UGL.Emulators/    # Process spawning, RetroArch config generator
│   ├── UGL.Hooks/        # MameHooker IPC, Hook of the Reaper
│   ├── UGL.Themes/       # AvaloniaThemeService, DynamicResource
│   └── UGL.Configuration/ # Config loaders, settings persistence
└── tests/
    └── UGL.Tests/
```

**Dependency rule:** UGL.Core has zero UGL dependencies. UGL.App is the sole composition root.

---

## 5. Portable Folder Structure

The packaged executable expects this layout. All paths are relative to the exe and fully configurable via Settings → Paths:

```
UltimateGameLauncher\
├── UltimateGameLauncher.exe
├── config\
│   ├── settings.json       # Root paths, audio, video, theme, language, card highlight
│   ├── games.json           # Games now carry CategoryIds (list) — see §7
│   ├── categories.json     # Includes ArtPath, VideoPath, BackgroundPath per category
│   ├── systems.json        # Includes optional per-system RomPath override
│   ├── emulators.json      # Includes IsRetroArchCore, RetroArchConfig
│   ├── themes.json
│   ├── audio.json
│   ├── controllers.json    # RawInput peripheral registry
│   └── hooks.json          # Hook Integration settings (tool choice, path, per-system overrides)
├── media\
│   ├── categories\         # Category card art and video (any filename)
│   ├── covers\             # Game cover art — {systemId}-{gameId}.{ext}
│   ├── backgrounds\        # Game background art — {systemId}-{gameId}.{ext}
│   ├── logos\              # Game logo/wheel art — {systemId}-{gameId}.{ext}
│   ├── marquees\           # Game marquee art — {systemId}-{gameId}.{ext}
│   ├── cardart\            # Generated/cleaned-up card art (§9.7) — {systemId}-{gameId}.{ext}
│   ├── screenshots\        # Up to 3 per game — {systemId}-{gameId}-1.{ext} .. -3.{ext}
│   ├── video\              # Game preview video — {systemId}-{gameId}.{ext}
│   ├── sounds\             # navigate.wav, confirm.wav, back.wav, error.wav
│   ├── music\              # Background music playlists
│   └── themes\
│
│   NOTE: flat, not media\games\* — an earlier version of this section (and the
│   scaffolder/README that generated the layout) documented a nested media\games\
│   convention that the actual save/load code (GamesConfigViewModel.CopyMediaFileAsync,
│   MediaAssetResolver) never implemented. Fixed in both the scaffolder and this doc
│   after a real user install was found with permanently-empty media\games\* folders
│   sitting alongside working art in the flat folders above — a game's identity is
│   encoded in its filename ({systemId}-{gameId}), not a per-game subfolder.
├── roms\                   # Default ROM root — sub-folders per system ID
├── emulators\              # Default emulator root
├── addons\                 # MameHooker, Hook of the Reaper, etc.
├── retroarch\              # ugl_override.cfg written here before each launch
└── logs\
    └── ugl.log
```

---

## 6. UI Specification

### 6.1 Card Design

Cards are full-height with no gradient overlay or text labels on the card surface. All text lives in the hint bar at the bottom.

- **Image:** `Stretch="Fill"` — fills exact card dimensions. Set directly via `Image.Source` in code-behind (not bound) to guarantee updates on live reload.
- **Placeholder:** Large dimmed first letter, shown when `CoverBitmap` is null.
- **Debug overlay:** removed entirely (was previously described as "hidden in release builds," but the code-behind unconditionally showed it whenever a cover loaded — no such gating actually existed).
- **Selection highlight:** Fully configurable — see §6.2. No longer a fixed 1px accent border.
- **No popout/scale effect.** Earlier revisions scaled the selected card up (`ScaleTransform`); this was removed in favor of a pure border highlight, which also eliminated a z-order problem where neighboring cards' opaque backgrounds clipped the scaled-up overflow.
- **Video:** `VideoView` inserted into `VideoContainer` border on the selected game card when `VideoPath` is non-null.
- **No gradient, no card-level text.** The card surface is 100% art or placeholder.

### 6.2 Card Selection Highlight

The selected-card border is a single, shared, user-configurable appearance applied identically by `GameCard` (Game Browser) and `CategoryCard` (Home Menu). Configured via Settings → 🌟 Card Highlight (§10.6).

**Settings (persisted to `AppSettings`):**
```
CardHighlightColor      string  hex, e.g. "#FFFFD700"
CardHighlightIntensity  double  0.1–1.0, applied as border opacity
CardHighlightStyle      string  "Solid" | "Pulsing"
CardHighlightThickness  int     2–5, pixels
```

**Live-settings bridge (`CardHighlightSettings`, static class in `UGL.App.ViewModels`):**
`GameCard` and `CategoryCard` are instantiated by Avalonia's `DataTemplate` system, not the DI container, so they cannot constructor-inject `IConfigurationService` the way DI-managed ViewModels do. `CardHighlightSettings` is a static snapshot of the four values above, loaded once at app startup (`MainWindowViewModel.InitializeAsync`) and updated live by `CardHighlightConfigViewModel` on every change — including before Save, so adjustments preview immediately on any currently-selected card. Both card code-behinds subscribe to `CardHighlightSettings.Changed` once in their constructor (not per `DataContext` change, to avoid stacking duplicate handlers against the small, bounded 5-instance VM pool — see §6.4).

**Rendering:** Applied via direct code-behind manipulation of the card's `Border.BorderBrush`/`BorderThickness` (not a XAML `Style` + `Classes` toggle — see the Avalonia Constraints entry in §13 on why this specific case regressed to direct manipulation). "Pulsing" style animates border opacity via a `DispatcherTimer` (50ms tick, ~1.5s sine-wave period, floored at 30% so it never fully vanishes) rather than an Avalonia `Animation`/`Transition`, for precise control over the floor/ceiling and period.

**Color wheel (`ColorWheelPicker`, `UGL.App.Views.Controls`):** A real draggable HSV picker, not a swatch list or plain sliders — built from two layered gradients rather than a hand-drawn bitmap or an external package:
- `ConicGradientBrush` sweeps the rainbow around the circle (hue).
- A white-center-to-transparent `RadialGradientBrush` layered on top gives the saturation falloff (center = desaturated, edge = fully saturated).
- A small ring (`CursorDot`) marks the current selection, repositioned in code-behind from bindable `Hue`/`Saturation` `StyledProperty`s (two-way).
- Pointer press/move/release drive selection via polar coordinates from the wheel's center; hue = angle (clockwise from top, matching the CSS `conic-gradient` convention), saturation = distance from center / radius.
- Brightness (HSV "Value") is not part of the wheel — it's a separate slider, since a 2D wheel can only represent two of the three HSV dimensions at once.
- Compact Hue/Saturation/Brightness sliders are also present alongside the wheel as a controller-friendly fallback; both input paths drive the same underlying `Hue`/`Saturation`/`Value` properties and stay in sync regardless of which one is used.

**Preview:** The Settings tab's live preview swatch binds to computed `IBrush`/`Thickness`-typed properties (`PreviewBrush`, `PreviewThickness`), not raw `string`/`int` values — see the relevant Avalonia Constraints entry in §13.

### 6.2a Favorites (Built-in Category)

`"favorites"` is a reserved category Id, treated specially at three layers:

- **Membership** (`JsonGameRepository.GetGamesByCategoryAsync`): computed from `Game.IsFavorite` rather than `Game.CategoryIds` — a game "joins" by being favorited, not by manual category assignment. Toggle it from the Game Editor or, in the Game Browser, with **X** on the currently focused game (persists immediately; a small gold star badge shows on favorited cards).
- **Home Menu visibility** (`HomeMenuViewModel.BuildCategoryListAsync`): the category row is always persisted (so its art sticks), but only ever shown — always as the first card — when at least one game is currently favorited. Re-evaluated on startup, on returning from the Game Browser, and whenever Settings closes, so it appears/disappears live as favorites change.
- **Categories tab**: auto-seeded once (`CategoriesConfigViewModel.InitializeAsync`) so its art/description can be customized immediately without manual setup. Its Delete button and Id field are both locked — critically, the Delete guard lives inside `DeleteCategoryAsync()` itself, not just the button's `IsEnabled`, since controller-driven Confirm calls that method directly and doesn't go through Avalonia's real interaction state the way a mouse click would.
- Excluded from the Games editor's category checkboxes (membership isn't manual, so a checkbox for it would be meaningless).

### 6.3 Card VM Pool

Both `HomeMenuViewModel` and `GameBrowserViewModel` maintain a **fixed pool** of 5 `CategoryCardViewModel` / `GameCardViewModel` instances. `RefreshVisibleCards` updates these instances **in-place** by Id comparison — never creates new VMs. This keeps `CategoryCard` / `GameCard` controls bound to the same VM object for the lifetime of the session, ensuring `PropertyChanged` subscriptions always reach the correct code-behind. This same bounded-pool property is what makes the single-subscription-in-constructor pattern for `CardHighlightSettings.Changed` (§6.2) safe.

### 6.4 Navigation

Games within any category (including Favorites) are always sorted alphabetically by title — `JsonGameRepository.GetGamesByCategoryAsync` re-sorts on every call rather than caching an order, so this stays correct automatically as games are added, removed, or re-categorized, with no separate re-sort step needed anywhere.

| Button | Home Menu | Game Browser | Filter Overlay | Settings |
|---|---|---|---|---|
| ← / → | Scroll categories | Scroll games | Browse pills | Sidebar → content; adjust value in a field; switch sub-tab where no value is adjustable |
| ↑ / ↓ | — | — | Switch rows | Move between menu rows / fields |
| A | Enter browser | Launch game | Select pill | Confirm / open editor / open keyboard |
| B | — | Return to Home | Close | Back one level (content → sidebar → close) |
| X | — | — | Reset filters | — |
| LB / RB | — | — | — | Switch sub-tab (Music/Sounds, Systems/Emulators) — always available regardless of field focus |
| Right stick | — | Scroll list (if overflowing) | — | Scroll whichever content is on screen |
| Start | Settings | Settings | — | Close |
| HOME / Tab | — | Filter overlay | — | — |

Right-stick scrolling (`ControllerAction.ScrollUp`/`ScrollDown`) reads the right stick's Y-axis with the same dead-zone/held-state mechanism as D-pad navigation, but with an immediate, faster repeat (40ms, no initial delay) rather than the 400ms-delay/120ms-repeat used for discrete menu navigation, since continuous scrolling should feel fluid rather than stepped. Each Settings tab's primary `ScrollViewer` is marked with `Classes="contentScroll"` specifically so the scroll handler (`ConfigEditorView.axaml.cs`) can find the correct one via a visual-tree walk without accidentally grabbing a `ListBox`'s own internal scrollviewer, which every `ListBox` has from its default control template.

### 6.5 Recommended Card Size Hint

Since "best" cover/art resolution depends on the user's actual screen and window size rather than a fixed constant, `GameCard`/`CategoryCard` each report their real on-screen pixel size (accounting for display scaling, via `TopLevel.RenderScaling`) whenever it changes, through a static bridge (`CardDimensionInfo`, same pattern as `CardHighlightSettings` in §6.2 and for the same reason — these cards aren't DI-constructed). A live hint using this value is shown under the Cover Art field (Games tab) and Art field (Categories tab). The Games-tab hint has no value until the Game Browser has rendered at least once in the current session; the Categories-tab hint always does, since the Home Menu renders before Settings can be opened.

---

### 6a.1 Unified Sidebar Pattern

Settings is a single `ConfigEditorViewModel` presenting a sidebar list (`MenuItems`, containing one row per tab plus a Quit row) and a content area showing whichever tab is active. Two focus zones:

- **Sidebar** (`IsContentFocused = false`): Up/Down cycles `SelectedMenuItem` through `MenuItems`; Right/Select enters content.
- **Content** (`IsContentFocused = true`): dispatched to the active tab's own `NavigateUp/Down/Left/Right`/`ConfirmAsync` methods; Back exits to the sidebar, then closes Settings on a second Back.

**Multi-level Back for nested sub-modes:** a handful of fields open their own nested sub-mode within an already-open editor — the Games tab's category checkbox grid, the Audio tab's track reorder sub-list. Back must close *just* the nested sub-mode first (`ConfigEditorViewModel.TryHandleContentBack()`, dispatching to e.g. `GamesConfigViewModel.TryExitCategoryOptions()`), falling through to the normal full `ExitContent()` (closing the whole editor back to the sidebar) only when nothing nested was open. Getting this wrong the first time around meant Back from the category grid closed the entire game editor, discarding in-progress unsaved changes — a real, reported regression, not just a rough edge. `Start`, by contrast, always fully closes Settings regardless of nested state, since its role is "toggle Settings closed," not "step back."

**Critical architectural decision:** all Settings navigation is driven by directly setting bound C# properties (`SelectedX`, `FocusIndex`, `IsOpen` booleans) from `MainWindowViewModel.OnControllerInput`, never through synthetic `KeyEventArgs` or `IKeyboardNavigationHandler`. Both were found unreliable/broken for this purpose in Avalonia 11.x during Milestone 15 — the FilterOverlay's existing pattern (proven in Milestone 5) was extended to the entire Settings surface instead.

### 6a.2 Input Routing Priority

```
1. If the virtual keyboard is open           → keyboard gets ALL input
2. If Settings is open                        → sidebar or content dispatch (§6a.1)
3. If the Filter Overlay is open               → Filter Overlay
4. Otherwise                                   → Home Menu / Game Browser
```

### 6a.3 Per-Tab Field Navigation

Every tab with editable fields uses the same convention: a `FocusIndex` (or named bool like `IsCategoryListFocused`) selects the active field; per-field computed `IsXFocused` bool properties drive a `Border` highlight via `Classes="cfgField" Classes.fieldFocused="{Binding IsXFocused}"`. **Both the default and highlighted appearance are defined as XAML `Style` setters, never as local `Border` attributes** — a local `Background`/`BorderBrush` attribute on the `Border` element always wins over a `Style` setter regardless of `Classes` match, which was the root cause of an early "highlight not visible" defect (§13).

Auto-scroll-into-view is centralized in `ConfigEditorView.axaml.cs`'s `ScrollFocusedFieldIntoView()` — a generic visual-tree walker finding the first element with class `fieldFocused`, deferred via `Dispatcher.UIThread.Post()` since layout must complete before `BringIntoView` is meaningful, subscribed to every child tab ViewModel's `PropertyChanged`.

---

## 7. Domain Models (UGL.Core)

### `AppSettings`
All properties are `set` (not `init`) so they can be updated at runtime without rebuilding the object.
```
MediaRootPath, RomsRootPath, EmulatorsRootPath, AddonsRootPath, LogsRootPath,
ActiveThemeId, DefaultCategoryId, Language, TargetFrameRate,
EnableBackgroundMusic, EnableNavigationSounds, MusicVolume, SoundVolume,
SoundNavigatePath, SoundConfirmPath, SoundBackPath, SoundErrorPath,
EnableVideoPreview, VideoPreviewDelayMs, VideoPreviewAudio, VideoPreviewVolume,
CardHighlightColor, CardHighlightIntensity, CardHighlightStyle, CardHighlightThickness
```

### `Category`
```
Id, Label, Order, IconKey,
ArtPath,        ← absolute or exe-relative, any filename, any drive
VideoPath,      ← absolute or exe-relative, any filename, any drive
BackgroundPath, ← optional full-screen background
AccentColor,    ← optional per-category accent override
Description     ← shown in hint bar subtitle
```

Managed on its own top-level Settings tab (🗂 Categories) rather than nested under Theme as in earlier revisions — see §10.1.

### `Game`
```
Id, Title, SystemId,
CategoryIds,   ← List<string>. A game may belong to any number of categories
               (e.g. both Racing and Multiplayer) and appears when browsing any of them.
               Was a single CategoryId (string) prior to Milestone 15 — see the
               migration note below.
RomPath, EmulatorId, Media (GameMedia),
Players, Genre, IsFavorite, LastPlayed
```

**Migration:** `JsonGameRepository` detects the legacy single-`categoryId` shape on load (a raw `JsonNode` pass over the array before deserializing into `List<Game>`) and converts it to the new `categoryIds` array shape in memory, so existing `games.json` files keep working without a manual edit. The migrated shape is written back to disk on the next save. `GetGamesByCategoryAsync` checks list membership (`CategoryIds.Any(...)`) rather than equality.

### `GameSystem`
```
Id, Name,
RomPath   ← optional per-system ROM directory override (any drive, network share)
           Leave empty → {AppSettings.RomsRootPath}\{Id}\
```

### `Emulator`
```
Id, Name, IsRetroArchCore,
ExecutablePath,   ← .exe path (standard) or .dll core path (RetroArch mode)
Arguments,        ← {rom} token (standard mode only)
SupportedSystems, Notes,
RetroArch         ← RetroArchConfig (non-null when IsRetroArchCore = true)
```

### `RetroArchConfig`
```
RetroArchExePath, CoreLibraryPath,
RunAheadFrames, RunAheadSecondInstance,
ShaderPresetPath, AdditionalConfig
```

### `Theme`
```
Id, Name,
AccentColor, BackgroundColor, SurfaceColor,
TextPrimaryColor, TextSecondaryColor, SelectionColor,
FontFamily, TitleFontSize, BodyFontSize,
CardCornerRadius, CardSpacing, SelectionScaleFactor, AnimationDurationMs
```

### `AudioPlaylist`
```
Id ("global" or {categoryId}), Name, Tracks, Shuffle, Volume
```

### `RawInputDevice`
```
HardwarePath (stable, persisted), FriendlyName, DeviceType,
PlayerIndex (1-based, 0=unassigned),
IsConnected (runtime only), RuntimeHandle (runtime only)
```

### `RawInputEvent`
```
HardwarePath, PlayerIndex, DeviceType,
DeltaX, DeltaY, AbsoluteX, AbsoluteY, IsAbsolute,
ButtonsDown, ButtonsPressed, ButtonsReleased, WheelDelta
```

### `ControllerAction` (enum, `UGL.Core.Models`)
```
None,
Select, Back, Secondary, Info,                    ← face buttons (A/B/X/Y)
CategoryLeft, CategoryRight,                       ← shoulder (LB/RB) — Settings sub-tab switching (§10)
FastScrollLeft, FastScrollRight,                   ← triggers (LT/RT)
NavigateLeft, NavigateRight, NavigateUp, NavigateDown,  ← D-pad / left stick
ScrollUp, ScrollDown,                              ← right stick (Milestone 15)
FilterOverlay,                                     ← HOME button
Start
```
The Input service (`XInputPollingService`, `KeyboardInputService`) translates raw hardware events into these semantic actions; the UI layer responds only to actions, never raw buttons or keys.

---

## 8. Service Layer

| Service | Interface | Implementation | Project | Status |
|---|---|---|---|---|
| Config loader | `IConfigurationService` | `JsonConfigurationService` | UGL.Configuration | ✅ |
| Game catalog | `IGameRepository` | `JsonGameRepository` | UGL.Data | ✅ |
| Emulator catalog | `IEmulatorRepository` | `JsonEmulatorRepository` | UGL.Data | ✅ |
| Audio playlists | `IAudioPlaylistRepository` | `JsonAudioPlaylistRepository` | UGL.Data | ✅ |
| XInput + keyboard | `IInputService` | `XInputPollingService` + `KeyboardInputService` | UGL.Input | ✅ |
| RawInput peripherals | `IRawInputService` | `RawInputService` | UGL.Input | ✅ |
| Peripheral registry | `IPeripheralRegistry` | `PeripheralRegistry` | UGL.Input | ✅ |
| Image cache | `SkiaMediaCache` | `SkiaMediaCache` | UGL.Media | ✅ |
| Media asset resolver | `MediaAssetResolver` | `MediaAssetResolver` | UGL.Media | ✅ |
| Audio service | `IAudioService` | `LibVlcAudioService` | UGL.Media | ✅ |
| Video preview | `IVideoPreviewService` | `VideoPreviewService` | UGL.Media | ✅ |
| Theme engine | `IThemeService` | `AvaloniaThemeService` | UGL.Themes | ✅ |
| Emulator launcher | `IEmulatorLauncher` | `ProcessEmulatorLauncher` | UGL.Emulators | ✅ |
| RetroArch config | `IRetroArchConfigGenerator` | `RetroArchConfigGenerator` | UGL.Emulators | ✅ |

**Not a DI service — a static bridge:** `CardHighlightSettings` (`UGL.App.ViewModels`) deliberately bypasses DI. `GameCard`/`CategoryCard` are instantiated by Avalonia's `DataTemplate` system rather than the DI container, so they cannot constructor-inject `IConfigurationService`. See §6.2.

---

## 9. Media System

### 9.1 Image Cache (`SkiaMediaCache`)

- **Lock-free reads:** `ConcurrentDictionary` — cache hits require no locking.
- **Parallel decoding:** Multiple `GetBitmapAsync` calls for different paths decode concurrently on the thread pool.
- **In-flight deduplication:** A secondary `ConcurrentDictionary<string, Task<Bitmap?>>` prevents the same path being decoded twice simultaneously.
- **Staleness detection:** Each cache entry stores `LastWriteUtc`. On cache hit, the file's current `LastWriteTime` is compared — stale entries are evicted and re-decoded automatically.
- **LRU eviction:** When count exceeds `MaxCachedImages` (default 200), the least-recently-accessed entry is evicted.
- **FileSystemWatcher:** One watcher per directory, created lazily on first `GetBitmapAsync` call for a path in that directory. Fires `ImageChanged` event on any file change.
- **`RaiseImageChanged(path)`:** Public method for programmatic notification (e.g. after saving category art in Settings).
- **Disposal:** All `Bitmap` instances are disposed on eviction. `ClearImageCache` disposes all — **do not call while Image controls still reference bitmaps.**

### 9.2 Media Asset Resolver (`MediaAssetResolver`)

**Category art/video:** Reads `Category.ArtPath` / `Category.VideoPath` directly. No filename convention. Any Windows-compatible filename. Any drive.

**Game assets:** Reads `GameMedia.CoverPath` etc. first (stored path). Falls back to `{systemId}-{gameId}.{ext}` slug convention in the appropriate subfolder under `MediaRootPath`.

**Path resolution:** Relative paths resolved against `AppContext.BaseDirectory`. Returns `null` if file does not exist.

### 9.3 Live Reload

**On file change (external):**
`FileSystemWatcher` → `OnFsChanged` → `EvictImage` → `RaiseImageChanged` → `HomeMenuViewModel.OnImageChanged` → `Dispatcher.UIThread.Post` → `LoadVisibleCoversAsync(_reloadCts.Token)` → `card.LoadCoverAsync` → `SetCoverImage(bitmap)` directly on `Image` control.

**On settings close:**
`MainWindowViewModel.CloseSettings` →
- `HomeMenuViewModel.RefreshCategoriesAsync` → reloads `categories.json` → updates `Category` on pool VMs by Id → `OnCategoryChanged` resets `CoverBitmap` → `LoadVisibleCoversAsync` decodes new art → `SetCoverImage` updates card immediately.
- `GamesConfigViewModel.RefreshCategoriesAsync` → reloads the category list and re-syncs the Games editor's category checkboxes (§10.2), so a category added/renamed/removed in the Categories tab shows up there too without reopening Settings.

### 9.4 Direct Image Assignment

`Image.Source` is **not data-bound** in `CategoryCard.axaml` or `GameCard.axaml`. It is set directly in code-behind via `SetCoverImage(bitmap)` which calls `img.Source = bitmap`. This bypasses Avalonia's binding system and guarantees updates regardless of reference equality or binding refresh state.

### 9.5 Manual Playlist/Track Control (Milestone 18, remapped in Milestone 22)

D-Pad Up/Down manually cycles the active music playlist from the Home Menu or Game Browser; D-Pad Left/Right skips to the next/previous track within the current playlist. Originally LB/RB (Milestone 18), remapped once Left Stick took over full menu navigation (§8b) and freed D-Pad for repurposing — LB/RB are now inert outside Settings, reserved for a future function.

`IAudioService.CyclePlaylistAsync(direction, currentCategoryId)` cycles through playlists that have at least one track and are either Global or assigned to `currentCategoryId` — a category's playlist is "locked" to that category and never appears while cycling anywhere else. **`currentCategoryId` must be `null` while at the Home Menu**, not whichever category card happens to be currently highlighted — a real bug that shipped and was caught after release: `MainWindowViewModel` was passing `_homeMenu.SelectedCategoryId` (the highlighted card, updated just by browsing left/right) instead of `null`, letting a category's playlist become cyclable just by scrolling past its card, without ever actually entering it. Only `_gameBrowser.ActiveCategory?.Id` (genuinely *in* that category's Game Browser) should ever produce a non-null value here.

`IAudioService.SkipToNextTrack()`/`SkipToPreviousTrack()` advance within `_shuffledTracks` the same way automatic track-end does, just user-triggered.

Both raise events (`PlaylistChanged`, `TrackChanged`) driving the same "now playing" toast in `MainWindowViewModel`, shown for 3 seconds via a `DispatcherTimer` — both views reach it via `$parent[Window].((vm:MainWindowViewModel)DataContext).NowPlayingPlaylistName`, the ancestor-binding-with-explicit-cast pattern also used for Audio's track-reorder buttons (compiled bindings, on by default in this project, need an explicit cast for a binding path that crosses view-model types). `TrackChanged` is deliberately **not** raised for the first track of a freshly-loaded playlist — that's already covered by `PlaylistChanged`, and firing both back-to-back would just have one overwrite the other's toast before it's readable.

### 9.6 Playlist System Rework (Milestone 21)

Complete replacement of the original "one playlist per category, hardcoded 'global' Id" design.

**Data model**: `AudioTrack` (`Id`, `Name`, `Path`) is a shared library — added once via Settings → Music → Track Library, referenced by any number of playlists via `TrackIds` rather than each playlist duplicating file paths. `AudioPlaylist.IsGlobal` and `CategoryIds` are independent fields, not mutually exclusive and not implied by a special Id — a playlist can be Global only, assigned to any number of Categories only, both, or (while still being built out) neither. `IsGlobal` is exclusive in *practice* (only one playlist should be THE global one), enforced by `AudioConfigViewModel.SaveAsync` clearing it on every other playlist when one is saved with it set — not enforced by the model itself.

**UI** (`AudioConfigViewModel`/`AudioConfigView`, single consolidated "Music" tab — an earlier two-tab split of Music/Track Library was collapsed after feedback that it didn't need to be two tabs): left pane is playlist selector (a real `ComboBox`, with Left/Right also cycling it directly via `SelectedItem` — not `SelectedValueBinding`, per the established ComboBox reliability lesson) plus Name/Global/Categories/Volume/Shuffle/Delete; right pane splits into Available Tracks (not yet in the playlist) and Assigned Tracks (in it), both maintained as separate `ObservableCollection`s rather than one filtered list.

**Drag-and-drop** moves tracks between the two panes for mouse users, alongside the existing enter-sub-mode/Up-Down/Confirm-to-move/Back-exit controller pattern operating on the same underlying `AssignTrack`/`UnassignTrack` methods. Implemented with `IDataObject`/`DragDrop.DoDragDrop` — **not** the newer `DataTransfer`/`DoDragDropAsync`, which is Avalonia v12-only and doesn't exist in this project's pinned 11.2.2. Hooked on `PointerMoved` (checking the left button is still held), not `PointerPressed` — official Avalonia guidance warns against starting a drag on every press, since a plain click would immediately trigger one and conflict with the ListBox's own selection handling.

**Volume**: `LibVlcAudioService` stores master volume (`_masterMusicVolume`) separately from `_musicPlayer.Volume` rather than wrapping it directly, applying `master × playlist.Volume` whenever a track loads (`ApplyEffectiveVolume`). See §13 for the real bug this replaced.

### 9.7 Card Art Generation System (Milestone 25)

Card Art is a **separate asset from Cover Art** (`GameMedia.CardArtPath`, its own
`media\cardart\` subfolder, its own `GameEditViewModel` field/preview) — both remain
independently editable, and a per-game `PreferCardArtAsCover` bool
(`MediaAssetResolver.ResolveCover`) lets the browse grid show Card Art instead of Cover
Art without deleting or overwriting the original cover.

**Two generators, deliberately not one.** Real usage showed "Generate Card" was doing
two different jobs that want different tradeoffs, so it's two explicit buttons in the
Games → Art sub-tab:
1. **🧹 Clean Cover for Card** — a 4-step pipeline over the scraped Cover Art (see
   below for the full history of what this button used to do and why each earlier
   version was replaced):
   1. Remove logo/text via a single-image ComfyUI masked-inpainting pass
      (`ScraperSettings.ComfyUiCleanupWorkflowPath`), run at the cover's own aspect
      ratio.
   2. Resize (not crop) to the current card resolution — `ResizeCoverFit`.
   3. Upscale/increase detail as needed — same `ResizeCoverFit` call.
   4. Composite the real, un-regenerated logo on top (top 25%, centered, drop
      shadow) — `CompositeLogo`, same step generator #2 uses.
   Falls back gracefully at every optional step: no cleanup workflow configured (or
   ComfyUI unreachable) skips step 1 entirely and still runs 2-4 on the plain cover;
   no logo scraped skips step 4. Never requires ComfyUI to produce *something* useful.
2. **🎨 Generate Poster Collage (ComfyUI)** — builds a literal collage from the game's
   own cover + up to 2 screenshots (`ImageStitch` → cover-fit crop → low-denoise
   img2img, NOT a fresh txt2img/IP-Adapter reinterpretation — an earlier IP-Adapter
   version reinterpreted references into an unrelated illustration style and was
   replaced for exactly that reason), then composites the game's real scraped logo on
   top (top 25% of the card, centered, drop shadow) — pasted as pixels, never
   AI-regenerated, since diffusion models can't reliably reproduce exact logo text.
   The workflow's negative prompt additionally forbids inventing new characters/scenes
   or adding any text/logo of its own.

**Generator #1's history — three rejected approaches before landing on detection-driven
masked inpainting.** Kept in full because each version looked plausible until tested:
- **v1: plain cover-fit crop, no AI at all.** Rejected — a hard center-crop to the
  card's tall aspect ratio cut off real content from a typical wider box cover instead
  of "reconstructing the art to fit," which was the actual ask.
- **v2: contain-fit + blurred-extension padding, still no AI.** Fixed the content-loss
  problem (`ResizeCoverFit` — this part survived and is still steps 2-3 of the final
  pipeline) but did nothing about baked-in logo/text, which is what the button is
  supposed to remove.
- **v3: whole-image img2img at a raised denoise.** Rejected twice, in two different
  ways: too low a denoise left the text intact; high enough to actually erase it also
  repainted/distorted the entire picture, since there's no way to target just the
  logo/text region with one global denoise value applied to the whole canvas.
  Separately, this version also pre-squashed the cover into the card's tall aspect
  ratio (~1:2.6) *before* inpainting, which distorted the art on its own, independent
  of the inpainting problem — stretching a normal ~2:3 box cover into that shape is
  lossy regardless of what happens afterward.
- **v4 (current): masked inpainting, at the cover's own aspect ratio, with the mask
  driven by real detection instead of a fixed region.** Both distortion sources from
  v3 are addressed by construction: `VAEEncodeForInpaint` + `ImageCompositeMasked`
  mean only the masked pixels can change at all (§ below on the mask itself), and the
  ComfyUI working canvas is now the cover's own size (rounded to a multiple of 8), not
  the card's shape — the card-shape reflow is entirely step 2's job, in C#, after the
  cleanup pass returns. An initial cut of v4 used a fixed top-28%-band mask (reasoning:
  "that's where the real logo will be pasted back anyway"), which worked for the one
  test cover on hand but doesn't generalize — not every cover puts its logo up top —
  so the mask itself was replaced with detection (below) rather than a fixed region.

**Where the v4 mask actually comes from.** Two local, no-cloud detectors
(`UGL.App/LogoRegionDetector.cs`, `UGL.App/TextRegionDetector.cs`) find where a given
cover's logo/text actually is, and their results are unioned into the mask
(`GamesConfigViewModel.BuildRegionsMask`):
- `LogoRegionDetector.LocateLogo` — multi-scale, alpha-masked template matching
  (`OpenCvSharp4`/`OpenCvSharp4.runtime.win`, `Cv2.MatchTemplate` with
  `TemplateMatchModes.CCorrNormed` + the logo's own alpha channel as the match mask) of
  the game's scraped logo asset (`Editor.LogoPath`) against the cover. This is a
  template-matching problem, not general logo detection, since UGL already has the
  exact asset scrapers extracted from the box art — it just needs to find where.
- `TextRegionDetector.DetectTextRegions` (optional —
  `ScraperSettings.TextDetectionModelPath`) — catches other stray badges (publisher/
  platform logos) that aren't an asset UGL has on file to match against. Runs a
  PP-OCRv3 "DB" text-detection ONNX model (e.g.
  `text_detection_en_ppocrv3_2023may.onnx` from `github.com/opencv/opencv_zoo`)
  through OpenCV's generic DNN module. **Hand-rolled, not a library call**: OpenCvSharp4
  (checked via reflection) does not expose OpenCV's own high-level
  `TextDetectionModel_DB` wrapper class, so this is a from-scratch port of that class's
  pre/post-processing, cross-checked against the model's own reference script
  (`opencv_zoo/models/text_detection_ppocr/ppocr_det.py`) for the exact preprocessing
  constants (ImageNet mean/std, 736×736 input, 0.3 binary threshold). One deliberate
  simplification: the reference algorithm's "unclip" step properly offsets each
  detected polygon outward via polygon clipping; this port pads the axis-aligned
  bounding rectangle instead, which is fine for a mask region (coverage, not precise
  geometry for actually reading the text) but means correctness here rests on
  standard DB-model conventions rather than an exact reimplementation — **not yet
  visually verified against real output**, since no interactive test loop was
  available while writing it. Expect this to need threshold/padding tuning once
  actually run against real covers.
- If neither detector finds anything (no logo asset, no text-detection model
  configured, or neither confident), the mask falls back to the original fixed
  top-28%-band guess (`BuildTopBandMask`) rather than doing nothing.

**Windows.Media.Ocr was tried first and abandoned for an environment reason, not a
design one.** The original plan for the "general badge" detector was Windows' built-in
OCR (`Windows.Media.Ocr`, ships with the OS, zero extra dependency) — but that requires
a version-qualified Windows SDK TFM (`net9.0-windows10.0.19041.0`), which in turn pulls
in Windows App SDK's MRT/PRI resource-packaging build tooling. That tooling's task
assembly (`Microsoft.Build.Packaging.Pri.Tasks.dll`) was not present under this
machine's .NET 10 SDK install, and fixing it means installing more Visual
Studio/Windows SDK workload components, not just a NuGet package. Reverted to plain
`net9.0-windows` and used the OpenCV DNN text-detection model instead, which needed no
TFM change at all. If a future environment has the full Windows App SDK tooling
installed, switching back to `Windows.Media.Ocr` would remove the "hand-rolled,
unverified" caveat above in exchange for a real, tested OCR engine.

**Two more real bugs found once the detection pipeline was actually live-tested with
its own diagnostic logging, both fixed this cycle:**
- `BuildRegionsMask`'s edge-feathering blur used the plain `new Bitmap(src, w, h)`
  constructor to downscale the mask before blurring it back up — that constructor's
  default resampling isn't area-averaged, so it could (and did, in testing) miss an
  isolated small-to-medium white rectangle on an otherwise black mask almost entirely,
  silently collapsing the mask to near-empty. Symptom looked identical to "the model
  can't do this" (nothing got regenerated anywhere) but was purely a masking bug —
  fixed by downscaling via `Graphics.DrawImage` + `HighQualityBicubic` (a real
  area-averaged resize) instead, with a gentler blur radius.
- Once the mask bug above was fixed and regions actually started getting regenerated,
  the *quality* of what got generated inside them was still bad — recognizable garbled
  pseudo-text instead of a clean fill. Root cause: `jedpointil_v7VAE.safetensors` (the
  only checkpoint installed in the test ComfyUI instance) is a general-purpose,
  anime-style txt2img model, and asking a model like that to "erase this text" via
  `VAEEncodeForInpaint` is a known-weak combination — it tends to hallucinate new
  text-like shapes rather than a plain reconstruction, since that's what visually fits
  the local context it was trained on. Fixed by switching the cleanup workflow to
  **Flux.1 Fill Dev**, a model purpose-built for mask-conditioned inpainting — its
  `InpaintModelConditioning` node takes the mask as direct model input rather than
  seeding partial noise into a region and hoping a general model reconstructs it well.
  This required a different node graph (`UNETLoader`/`DualCLIPLoader`/`VAELoader` +
  `DifferentialDiffusion` + `FluxGuidance` + `InpaintModelConditioning`, verified
  against ComfyUI's own official Flux Fill Dev example workflow rather than guessed)
  but no C# changes beyond the default denoise (1.0, Flux Fill's own documented
  default, vs. the old approach's 0.95) — the same `{{IMAGE_1}}`/`{{IMAGE_2}}`/
  `{{PROMPT}}`/`{{DENOISE}}`/`{{WORK_WIDTH}}`/`{{WORK_HEIGHT}}` token substitution
  mechanism carried over unchanged. Not yet confirmed by the user against a real
  Flux Fill install — Flux is a ~23GB download the user needs to do themselves.

**Dynamic resolution, not a hardcoded constant.** Both generators call
`GamesConfigViewModel.GetTargetCardResolution()`, which reads
`CardDimensionInfo.GameCardPixelSize` (§6.5 — the actual live on-screen card size) and
falls back to a fixed 756×1968 only if no card has rendered yet this session. A shared
`ResizeCoverFit(bytes, width, height)` helper (cover-fit crop, same `System.Drawing`
machinery as `CompositeLogo`) forces the *final* output to this value regardless of
whatever resolution the ComfyUI workflow file's own internal sampling nodes use — this
means the workflow JSON never needs to be hand-kept in sync with the card grid's actual
size.

**Screenshots as a persisted multi-file asset — new pattern.** Every other media type
(Cover/Logo/Marquee/Video/CardArt) is a single file per game; screenshots are the first
asset to need a *list*. `GameMedia.ScreenshotPaths` (`List<string>`), an
`ObservableCollection<string>` mirror on `GameEditViewModel`, up to 3 downloaded to
scratch on Scrape and copied to `media\screenshots\{systemId}-{gameId}-{n}.ext` on Save
(`CopyMediaFileAsync` gained an optional `suffix` parameter for this — no new copy
method needed). `MediaAssetResolver.ResolveScreenshots(Game)` returns the list. This is
what actually fixed "must re-scrape every session just to regenerate card art" — the
poster generator now reads local files (`Editor.CoverPath` + `Editor.ScreenshotPaths`)
first, falling back to re-downloading alternate-artwork URLs from the last in-session
scrape only as filler.

**Scraper platform coverage.** All three sources (IGDB, ScreenScraper, TheGamesDB) now
surface *all* screenshots/artwork/alt-covers instead of discarding everything but the
first result — a change made specifically to give the collage generator real material
to work with. Clean logo art (`LogoPath`) is only available from ScreenScraper
(`wheel`/`wheel-hd`) and TheGamesDB (`clearlogo`) — **IGDB has no clean-logo asset type
at all**, a data limitation, not a bug; a missing logo is always cosmetic (the poster
still generates, just without the overlay — an earlier status message wording made
this read like a failure and was reworded). IGDB is the only source of the three that
attempts platform-filtered search at all (`platforms.name ~ *"{hint}"*`, falling back
to unfiltered only if the filtered query comes back empty); ScreenScraper and
TheGamesDB search by title only, with no platform narrowing — a known, larger gap
(each would need its own platform-ID mapping researched against that API), not fixed
this cycle.

**Games tab restructure (Settings/Art sub-tabs).** Mirrors Audio's Music/Sounds split
exactly: two independent focus-index cycles (`SettingsFocusIndex`/`ArtFocusIndex`, each
with its own position count and reachable Save/Cancel), `IsSettingsTabActive`/
`IsArtTabActive`, `SwitchSubTabLeft/Right` wired through `ConfigEditorViewModel`'s
existing LB/RB dispatch (already had cases for Audio/Systems — Games was just a third
case, no new top-level `Tab` enum or DI plumbing). The Art sub-tab is a two-column
`Grid`: compact single-line fields on the left, one large shared preview panel
(`FocusedArtPreview`) on the right that shows whichever field/button currently has
focus — replacing what had been small inline thumbnails per field, which became
unreadable once Card Art and screenshots were added on top of Cover/Logo/Marquee. The
game list panel is hidden entirely (not just squeezed into a fixed column) while the
editor is open, giving the full window width to whichever sub-tab is active.

**Delete confirmation, app-wide.** A reusable `ConfirmDialogViewModel` (`UGL.App`,
same static-overlay-plus-top-of-input-priority architecture as the virtual keyboard,
§9a) gates all five destructive delete actions in Settings (Game, System, Emulator,
Category, Playlist) behind an explicit Yes/No — defaults to **No** highlighted so an
accidental repeat button-press can't confirm a delete. This was previously only a
concern for Games/Systems/Categories/Audio individually; it's now one shared component
any tab can call `_confirmDialog.Open(message, onConfirm)` against.

---

## 9a. Virtual Keyboard (Milestone 15)

A full on-screen keyboard (`VirtualKeyboardViewModel`, `UGL.App.ViewModels`) lets every text field in Settings be edited entirely with a controller — no physical keyboard required.

- **Layout, 5 rows:** `1234567890` / `qwertyuiop` / `asdfghjkl` / `Shift z x c v b n m Back` / `◀ Space ▶ Done Cancel`.
- **Navigation:** Up/Down/Left/Right move `RowIndex`/`KeyIndex` with per-row wraparound and clamping when adjacent rows have different lengths.
- **Cursor:** `CursorPosition` (int index into the buffer) drives insert-at-cursor typing and delete-before-cursor backspacing, rather than always appending/trimming at the end. The dedicated `◀`/`▶` keys move it without modifying text. The buffer display shows a visible cursor bar (accent-colored) between the text before and after the cursor — two separately-bound `TextBlock`s (`TextBeforeCursor`/`TextAfterCursor`, computed and re-raised whenever `BufferText` or `CursorPosition` changes) rather than one, since Avalonia has no bindable native text-caret concept for a plain `TextBlock`.
- **Shift:** toggles case on all letter keys via an `OnIsShiftedChanged` partial hook.
- **Opening:** any field wanting text input calls `_virtualKeyboard.Open(label, currentValue, v => property = v)`; the commit callback only fires on `Done` (`Cancel` discards).
- **Input routing:** the keyboard sits at the top of the input priority stack (§6a.2) — while open, it consumes all controller input regardless of what else is on screen.
- **No symbol keys** (only a–z, 0–9, Space, Shift, Back, Done, Cancel, ◀, ▶) — a known, accepted limitation for now.

---

## 10. Configuration Editor (Settings)

Eight tabs plus Quit, accessible via Start button, navigated per §6a:

### 10.1 🗂 Categories Tab
Two-column layout: category list (left), field editor (right). `IsCategoryListFocused` gates whether Up/Down cycles the list or the field editor's `CategoryFocusIndex`. 11 editable fields: Id, Label, Order (with a live hint showing which Order numbers other categories already use, excluding the one currently selected), Accent, Art (Browse), Background (Browse), IconKey, Description, Add, Save, Delete. `AddCategory()` must add the new blank `Category` to the `Categories` collection *before* assigning it as `SelectedCategory` — see §13 for why assigning a not-yet-listed item directly breaks Save. **X while the list has focus adds a new category directly**, bypassing the need to route through an existing category's fields first (§13). The built-in Favorites category (§6.2a) is auto-seeded here on first load, with its Delete button and Id field both locked.

### 10.2 🎮 Games Tab
Full game catalog editor. Browse/drag-drop for all media fields. **Category is a multi-select checkbox list** (`ItemsControl` of `CheckBox`, bound to `Editor.CategoryOptions`), not a single dropdown — a game can belong to any number of categories (§7). `GameEditViewModel.SyncCategoryOptions()` rebuilds the checkbox list from the current category catalog while preserving which are already checked; called on load and whenever the category catalog changes (§9.3). System and Emulator selectors cycle with Left/Right; Players adjusts 1–8 with Left/Right; Favorite toggles with Confirm. The category checkbox grid itself is fully controller-navigable as of Milestone 15h: Confirm enters it, Up/Down moves the highlight between checkboxes, Confirm toggles the highlighted one, Back exits back to the field list (§6a.1) — the editor stays open, so in-progress category changes aren't lost. On save: copies files into the flat `media\` subfolders from §5, renamed to `{systemId}-{gameId}.{ext}` (or `-1`/`-2`/`-3` suffixed for screenshots) — not the original filename — writes `games.json`, evicts image cache.

### 10.3 🖥 Systems Tab
Two inner tabs:
- **Systems** — add/edit/delete `GameSystem` entries.
- **Emulators** — full editor with `IsRetroArchCore` toggle. Standard mode: exe path + `{rom}` args. RetroArch mode: core `.dll` path, RetroArch exe path, run-ahead frames, second instance, CRT shader preset, additional config.

Both sub-tabs use a 280px list (left) + editor (right) layout.

### 10.4 🎵 Audio Tab
Two inner tabs, switchable via LB/RB or Left/Right:
- **Music** — playlists, per-category overrides, track reorder. Fully controller-navigable as of Milestone 15: a list/field-mode toggle (same convention as Categories) covers Enable Music, Master Volume, category override, playlist Volume/Shuffle, and a track sub-list (Confirm enters it, Up/Down selects a track, Left/Right reorders it, Confirm removes it).
- **System Sounds** — per-sound file paths with Browse + ▶ Test, video preview audio toggle + volume. Fully controller-navigable (13 flat positions).

### 10.5 🎨 Theme Tab
- Theme selection + Apply (live, no restart).
- Category management moved to its own tab (§10.1) as of Milestone 15 — this tab is theme selection only.

### 10.6 🌟 Card Highlight Tab (new, Milestone 15)
Configures the shared selection-highlight appearance described in §6.2: HSV color wheel + Hue/Saturation/Brightness sliders (kept in sync with the wheel), Border Width slider (2–5px), Intensity slider (0.1–1.0), Solid/Pulsing style toggle, and hex/HSL/RGB readouts. Live preview swatch. Layout is two-column — wheel, preview, and Save together on the left; sliders and readouts on the right — deliberately compact after an earlier, taller single-column revision proved unwieldy.

### 10.7 📁 Paths Tab
- **Global root folders:** Media Root, ROMs Root, Emulators Root, Addons Root, Logs Root. Each with Browse (folder picker). All paths portable (relative) by default, can be set to any absolute path or UNC share.
- **Per-system ROM paths:** List of all systems with optional ROM directory override. Leave blank to use `{RomsRootPath}\{systemId}\`. Any drive, any network share.

### 10.8 🕹 Controllers Tab (renamed from "Peripheral Hooks")
RawInput device list with Rescan and per-device Player Index assignment (Left/Right adjusts). See §11a for the underlying hardware layer this tab manages.

---

## 11. Emulator Launcher

`ProcessEmulatorLauncher` (in `UGL.Emulators`):

1. Resolves `Emulator` from `IEmulatorRepository` by `game.EmulatorId`
2. **Standard mode** (`IsRetroArchCore = false`): resolves `.exe`, substitutes `{rom}` in `Arguments`, spawns process.
3. **RetroArch mode** (`IsRetroArchCore = true`): calls `IRetroArchConfigGenerator.GenerateAsync` → writes `retroarch\ugl_override.cfg` → launches `retroarch.exe -L "{core.dll}" "{rom}" --appendconfig "{override.cfg}"`
4. On exit: calls `IRetroArchConfigGenerator.Cleanup()` (deletes override cfg), raises `EmulatorExited`, restores window and audio.

`MainWindow.axaml.cs` watches `IsEmulatorRunning`:
- `true` → `WindowState = Minimized`
- `false` → `WindowState = FullScreen` + `Activate()` + `Focus()`

---

## 11a. RawInput Hardware Layer (Milestone 15)

`RawInputService` (`UGL.Input`) runs a Win32 message pump on a dedicated thread to receive `WM_INPUT_DEVICE_CHANGE` notifications for real-time hot-plug detection, alongside the on-demand `EnumerateDevices()` used by the Controllers tab's Rescan button.

- **Friendly name resolution:** walks from a RawInput device handle → hardware interface path → device instance ID (`CM_Locate_DevNodeW`) → up the device tree via `CM_Get_Parent` (from a generic "HID-compliant game controller" collection to the parent USB/Bluetooth device that carries the real product name — what Device Manager's "by connection" view shows) → `DEVPKEY_Device_FriendlyName`, falling back to `DEVPKEY_Device_DeviceDesc`. Indirect string references (`@driver.dll,-1234`) are resolved via `SHLoadIndirectString`.
- **HID capability filtering:** distinguishes a real controller input surface from an auxiliary interface (dongle pairing channel, vendor telemetry) that also happens to enumerate as a RawInput HID device, by opening the device (`CreateFile`, query-only) and checking its actual `HIDP_CAPS` (`HidD_GetPreparsedData` + `HidP_GetCaps`) for a recognized Usage Page/Usage and real input buttons/axes — not string heuristics on the name.
- **Message-only window:** created via `RegisterClassW`/`CreateWindowExW` — **both explicitly forced to their Unicode entry points**, since `WNDCLASS`'s class-name field is marshaled as Unicode (`[StructLayout(CharSet = CharSet.Unicode)]`) but neither `DllImport` specified a `CharSet`, which defaults to Ansi and silently binds to the `...A` exports. See §13 for the full failure mode this caused and how it was found.

---

## 11b. Hook Integration (Milestone 16)

A new project, `UGL.Hooks`, manages the lifecycle of an external output-hook tool — **MameHooker** or **Hook of the Reaper** — configured via the Settings tab **🔌 Output Hooks**.

**Scope, deliberately limited:** both tools listen on the network port the emulator itself broadcasts to (the MAME-established output-signal standard — TCP port 8000, UDP port 8001), and resolve their own per-game configuration entirely independently. UGL's role is process lifecycle only — launch the configured tool hidden/background just before the emulator starts (`ProcessEmulatorLauncher.LaunchGameAsync`, after all validation/file-existence checks pass — not before, since several of those checks return early or throw, and starting the hook tool any sooner would leave it orphaned in the background on a failed launch attempt), and stop it when the emulator exits (`OnProcessExited`, plus every other exit/failure path in `LaunchGameAsync` — `Process.Start()` returning false, the top-level catch block). UGL does not parse, forward, or otherwise get involved in the actual output-signal traffic.

**Config** (`config/hooks.json`, own dedicated repository — `IHookSettingsRepository`/`JsonHookSettingsRepository` — same pattern as `games.json`/`audio.json`, not folded into `AppSettings`): enable toggle, tool selection (None/MameHooker/HookOfTheReaper), executable path, startup delay (ms — gives the tool time to start listening before the emulator's first signal fires), and a per-system "disable for this system" list. The Settings tab's per-system checklist uses the same enter-sub-mode/Back-exits-it pattern as Categories and Audio (§6a.1).

---

## 11c. Portable-Path Independence (Milestone 18)

Every path the app stores — emulator/RetroArch executables, ROMs, game/category media, hook tool executables, sound effects, music tracks, BIOS files, bezel images — is converted to a path relative to the app's own base directory when the target is inside the app's own folder tree, via a single shared helper: `UGL.Core.Utilities.PortablePathHelper`.

- **`ToPortablePath(absolutePath)`** — called at every save point. Converts to relative when the target is inside the app's base directory; returns the original absolute path unchanged when the target is on a different drive, or reachable only via a `..\` prefix (which isn't truly portable either, since it still depends on the position of something outside the part of the tree that actually moves together).
- **`ToAbsolutePath(storedPath)`** — called at every read point, before the path is actually used (file existence checks, `Process.Start`, media loading). Resolves a relative path against the app's base directory; returns an absolute path unchanged.

This matters because a file picker always returns an absolute path — without this conversion, a portable install (e.g. on a USB drive) would silently break the moment it landed on a different drive letter or a different machine's port, even for files kept inside the app's own recommended folder structure.

**Two real, latent bugs surfaced and were fixed while doing this sweep** — both were "resolve a path back to absolute before using it" steps that either didn't exist or were incomplete, and had been silently relying on stored paths always happening to already be absolute:
- `HookLauncher.StartAsync` used the stored executable path completely raw (no resolution at all).
- `LibVlcAudioService.PlayCurrentTrack` never resolved a music track path either — only the navigation-sound path (`PlaySoundFromPath`) had a (partial) version of this.

Both are fixed now and use the shared helper. See §13 for the general lesson.

## 11d. BIOS Files and Bezels (Milestone 18)

**BIOS**: `Emulator.BiosPaths` (`List<string>`) holds the BIOS file(s) an emulator/core needs, applying to every game launched through it — the common case, configured once per emulator (Settings → Systems → Emulators, same enter-sub-mode/Up-Down/remove/Back-exits pattern as Audio's track list). `Game.BiosOverridePaths` is a rare, nearly-always-empty per-game override for the exception case (e.g. a region-specific BIOS variant), same UI pattern on the Game editor. `ProcessEmulatorLauncher` checks (game override takes priority when non-empty) and logs a warning for any missing file before every launch — this never blocks the launch, since the emulator itself is the actual authority on whether a given BIOS is required.

**Bezels**: `GameSystem.BezelPath` is the default bezel/overlay image for a system (aspect-ratio/cabinet-art concept, not emulator-specific — the same system might be played through more than one emulator but usually wants the same bezel regardless). `Game.BezelOverridePath` is the rare per-game override, same convention as BIOS.

For RetroArch-launched games, `RetroArchConfigGenerator` wires the configured bezel into RetroArch's own native overlay system, which is two levels: the main generated `ugl_override.cfg` points at a small overlay `.cfg` file (auto-generated alongside it, `ugl_bezel_overlay.cfg`), which in turn points at the actual bezel image via a minimal static full-screen overlay (`overlays = 1`, `overlay0_overlay = "<path>"`, `overlay0_full_screen = true`, `overlay0_descs = 0` — no interactive button regions, since this is just static bezel art). Both generated files are cleaned up on emulator exit, same as the existing override config.

**Standalone (non-RetroArch) bezel rendering is explicitly not implemented — researched, deliberately deferred.** Two approaches exist in the community (LaunchBox's "BezelLauncher" plugin and ReShade-based shader injection); neither is a good fit for UGL to build speculatively right now:

- **Window resize + overlay** (BezelLauncher's approach) — resize/reposition the emulator's own window into a transparent cutout in a bezel image, shown as a separate overlay window. Explicitly excludes RetroArch and MAME (both already have native overlay systems, matching UGL's own architectural split). Requires true windowed mode, not fullscreen — a real conflict with the fullscreen-for-lowest-latency preference most cabinet setups want. Even BezelLauncher's own author states it's untested on a real cabinet, only a windowed desktop PC. Would need a per-emulator "recipe" (resize/reposition flags or window-manipulation quirks), since there's no universal API for this.
- **Shader injection via ReShade** — hooks the emulator's own DirectX/OpenGL/Vulkan rendering pipeline to composite the bezel into the actual frame. Pixel-perfect and fullscreen-compatible, but is a manual, per-emulator setup task for the user (install + configure ReShade + a bezel shader per emulator) — not something UGL could realistically automate or generate, so it's outside UGL's scope as a *feature* regardless of technical feasibility.

If revisited, the recommended path is starting narrow — one specific standalone emulator, confirmed to support true windowed mode at a fixed known size, prototyped before any attempt to generalize.

---

## 11e. In-App Update System (Milestone 19)

New project `UGL.Updates`, implementing `IUpdateService`. Checks GitHub Releases (`GET /repos/{owner}/{repo}/releases/latest` — public, unauthenticated) for a version newer than the running app, comparing as parsed `Version` objects (not string comparison — `1.10.0` must correctly beat `1.9.0`). `CurrentVersion` reads whatever's embedded in the entry assembly via `<Version>` in `UGL.App.csproj`, so a release requires bumping that property (`scripts\build-release.ps1` overrides it at publish time via `-p:Version=`, so the csproj value only matters for local dev builds).

**Two trigger paths, matching what was asked for:** `CheckForUpdateInBackground()` runs once on startup (non-blocking, any failure — no internet, GitHub down, rate-limited — is logged and silently treated as "no update," never surfaced as disruptive), and a manual "Check for Updates" button is always available on the **🔄 Updates** Settings tab regardless of the background result.

**Confirm-then-apply, not fully automatic:** a found update shows release notes and requires an explicit "Install and Restart" (or "Not Now," which just dismisses for the session — it's offered again on the next check, nothing is remembered as "permanently declined"). `ApplyUpdateAsync` downloads the release's `.zip` asset, extracts it, then — since a running `.exe` cannot overwrite itself — writes and launches a small batch script that waits for the current process to actually exit (`Environment.Exit(0)` right after launching it), copies the extracted files over the install directory via `xcopy /EXCLUDE`, relaunches, and cleans up after itself. The exclude list is a duplicate of `AppFolderScaffolder.Folders`/`build-release.ps1`'s user-data list (`config`, `roms`, `emulators`, `bios`, `bezels`, `addons`, `retroarch`, `logs`, `media`) — kept in three places now, all with comments cross-referencing each other, since there's no single shared assembly all three could cleanly depend on without introducing an awkward reference direction.

**Notification UI:** a top-anchored banner in `MainWindow.axaml` itself (not duplicated per-screen the way the "now playing" toast is) — `MainWindowViewModel` owns `IsUpdateNotificationVisible`/`UpdateNotificationText` directly, since `MainWindow`'s own DataContext already is `MainWindowViewModel`, no ancestor-binding indirection needed. Stays visible (not auto-hidden) until Settings is opened, since a missed update notification is more consequential than a missed "now playing" toast.

## 11f. Release Process (Milestone 17)

`scripts\build-release.ps1` — `dotnet publish` (self-contained, single-file, `win-x64`), then strips the same user-data folder list described above from the publish output before zipping, in case the publish output folder was ever run/tested from directly before packaging (which would let `AppFolderScaffolder` populate it with real `roms\`/`bios\`/etc. content). The strip list is deliberately narrow (specific folder names, not a broad "keep only known extensions" filter) — LibVLC ships native `.dll` files and a `plugins\` folder alongside the managed assembly that a naive extension allowlist wouldn't recognize as legitimate and could silently delete, breaking audio/video playback in a way that wouldn't be obvious until someone actually tried to use it.

`UGL.App.csproj` originally had a `<Content Include="..\..\config\**\*">` rule (a local-dev convenience, copying the repo-root `config\` folder into every build for easy local testing) that risked baking personal library/settings data into a public release. It was removed entirely (Milestone 21) rather than just stripped from publish output — it turned out to also be silently resetting the *dev* build's runtime `config\` on rebuilds, which was the actual root cause of an "auto-play doesn't work" report that took several rounds to trace (see §13).

Release: tag `vX.Y.Z` on GitHub (the `v` prefix matches convention; `GitHubUpdateService` strips it before comparing), release notes in the description (surfaced verbatim in the Updates tab), the built ZIP attached as the release asset. v0.1.0 and v0.2.0 both published; the update mechanism (§11e) confirmed working end-to-end against a live release — an older running copy correctly detected, downloaded, and applied the newer one.

## 11g. Controller Remap (Milestone 22)

Left Stick now drives all four navigation directions (`NavigateLeft/Right/Up/Down`) — previously it only handled Left/Right, with D-Pad as the sole source of Up/Down. D-Pad is fully repurposed for music control now that Left Stick covers navigation: Up/Down cycles the playlist (`PlaylistNext`/`PlaylistPrevious`), Left/Right skips tracks (`TrackNext`/`TrackPrevious`) — new `ControllerAction` values, mapped in `XInputPollingService.ProcessControllerState`. LB/RB (`CategoryLeft`/`CategoryRight`) keep working for Settings sub-tab switching (unaffected, a separate dispatch branch) but are otherwise unhandled outside Settings — deliberately freed for a future function rather than removed from the enum.

## 11h. Per-Game Peripheral Disable (Milestone 23)

`Game.DisabledDeviceTypes` (`List<RawInputDeviceType>`) — configured via a checkbox grid in the Game editor, covering the same "assignable" types `RawInputService.IsAssignablePeripheral` already recognizes (Gamepad, Lightgun, Wheel, Spinner, Trackball). `RawInputService.SetDisabledDeviceTypes` silently drops input from a disabled type *before* `RawInputReceived` is even raised, rather than expecting every consumer to filter it themselves.

`ProcessEmulatorLauncher` applies the configured set only *after* a confirmed successful process start, not any earlier — every failure path before that point (`return -1`) would otherwise leave peripherals disabled with nothing around to re-enable them, since `OnProcessExited` (which resets to an empty set) never fires for a launch that never actually started. Re-enabled unconditionally on exit, alongside the existing hook/RetroArch cleanup.

Scope is deliberately narrow: RawInput peripherals only (lightguns, wheels, spinners, trackballs), never the primary XInput controller used for menu navigation.

---

`AvaloniaThemeService` loads `themes.json`, builds a `ResourceDictionary` with all `UGL.*` keys, and merges into `Application.Resources` on the UI thread. All AXAML binds via `{DynamicResource UGL.XxxKey}`. Cards use `Application.Current.FindResource()` in code-behind for brush lookups so selection borders also theme correctly. Three seed themes ship: Default Dark, Arcade Neon, Minimal.

---

## 13. AI Development Rules

1. Never generate the entire application in one response.
2. Build one milestone at a time — do not proceed until verified by the user.
3. Explain architectural approach before writing code.
4. Never overwrite or regress completed milestones.
5. When delivering files, always package as `.zip` with `src/` folder structure matching the solution.
6. **Never rely on zip extraction overwriting stale files.** If a file is known to be stale on disk, explicitly instruct the user to delete it first, or verify with a `Select-String` command.
7. After delivering a fix, always provide a verification command so the user can confirm the correct content is on disk before rebuilding.
8. **Before editing a file, confirm you have the current version, not a stale snapshot from an earlier handoff package.** Milestone 15 shipped one real regression this way — a RawInput fix was correctly written but applied on top of a pre-friendly-name-resolution copy of the file pulled from an early handoff zip, silently reverting unrelated, already-shipped work. When in doubt, ask for the current file rather than assume a bundled/earlier copy is up to date.

### Avalonia Constraints (all confirmed in production)
- No `DataTrigger`, `Style.Triggers`, `RelativePoint X/Y/Unit` — WPF only
- `IControl` removed in Avalonia 11 → use `Control`
- `BoolConverters.ToBoolean` cannot produce a `Brush` — use code-behind
- Gradient stop `Color` cannot bind `DynamicResource` brush — keep static
- `DataTemplate DataType` inside `ItemsControl` must match item type, not parent VM
- `Image.Source` binding may not update when bitmap reference is unchanged — set directly in code-behind
- `ObservableObject` skips `PropertyChanged` when value is same reference — manage backing field manually and always call `OnPropertyChanged` for bitmap properties
- `FindControl<T>()` may return null if called before visual tree is attached — cache control references in constructor after `InitializeComponent()`
- `FileSystemWatcher` fires on background threads — always dispatch to `Dispatcher.UIThread` before touching UI or `ObservableCollection`
- `ClearImageCache()` disposes `Bitmap` objects — do not call while `Image` controls still reference those bitmaps
- `AppContext.BaseDirectory` differs between `dotnet run` (project dir) and published exe (exe dir) — test with actual run output paths
- All cross-project service classes must be `public`
- Config files use `<Content>` not `<None>` in `.csproj`
- No `SupportedOSPlatformVersion`, no `WithInterFont()`
- All projects target `net9.0-windows`
- LibVLCSharp aliases: `using LibVlcCore = LibVLCSharp.Shared.Core; using LibVlcMedia = LibVLCSharp.Shared.Media;`
- Background helper processes (`mamehook.exe`, `HookOfTheReaper.exe`): always `CreateNoWindow = true`, `WindowStyle = Hidden`
- **A local XAML attribute (`BorderBrush="..."`, `BorderThickness="0"`, etc.) always wins over a `Style` setter, regardless of `Classes` match.** Both the default and toggled appearance must live in `Style` setters, never one-as-attribute-one-as-style — this caused an early card-highlight defect where the style was correctly written but silently had no visible effect.
- **Direct code-behind manipulation of a property has the same override problem as a local attribute.** If a control's code-behind sets `Border.BorderBrush`/`BorderThickness` directly (e.g. in a `PropertyChanged` handler), it will silently override a `Style`+`Classes` toggle on every change regardless of which was written more recently — pick one mechanism per property and keep it consistent; don't split the same visual state between a style and code-behind.
- **`ListBox.SelectedItem` (two-way bound) silently rejects/resets any value not actually present in its `ItemsSource`.** A "new item" workflow must add the new object to the source collection *before* assigning it as the selection, or the assignment appears to silently do nothing (e.g. a Save button whose `IsEnabled` depends on selection being non-null stays disabled).
- **Binding a raw primitive (`string`, `int`) to a differently-typed property (`IBrush`, `Thickness`) reliably converts via Avalonia's built-in `TypeConverter` on the *initial* bind, but was not observed to reliably reconvert on every subsequent value change** in this codebase's testing. Where a bound value needs to update live and repeatedly, expose a genuinely-typed computed property (`IBrush`, `Thickness`, etc.) instead of relying on implicit conversion.
- **`ConicGradientBrush` + `RadialGradientBrush`, layered, can render a real HSV color wheel** without a hand-drawn bitmap or an external package (`Avalonia.Controls.ColorPicker` is a separate NuGet package requiring a version-matched reference and an `App.axaml` style include; the layered-gradient approach needs neither). Exact 0°/sweep-direction convention for `ConicGradientBrush` was not independently verified against Avalonia's source — the CSS `conic-gradient` convention (0° = up, clockwise) was assumed and matched between rendering and pointer-interpretation math, which keeps the picker functionally correct even if that assumption is imprecise; only the wheel's cosmetic rotation would be affected.
- **`RadialGradientBrush.Radius` is deprecated** in this Avalonia version — use `RadiusX`/`RadiusY` instead, both expressed as relative percentages (e.g. the old `Radius="0.5"` becomes `RadiusX="50%" RadiusY="50%"`), even when the rest of the brush uses absolute values.
- **`Grid.RowSpacing`/`ColumnSpacing` do not exist in this project's pinned Avalonia version (11.2.2)** — they were only added in an 11.3 alpha build. A web search confirming a property exists in Avalonia's *current* docs is not the same as confirming it exists in *this project's* pinned version; for anything version-sensitive, cross-check against the actual installed version, not just the latest documentation. Use explicit `Margin` on each grid child instead — works identically on any version.
- **A brand-new project added to the solution needs its NuGet package versions pinned to match the rest of the solution explicitly** — `dotnet add package` with no `--version` grabs whatever the currently-installed SDK resolves as latest (here, a .NET 10 SDK installed alongside the project's .NET 9 target resolved `Microsoft.Extensions.Logging.Abstractions` to 10.0.9), which triggers a NU1605 downgrade-conflict error the moment anything else in the solution references both the new project and the same package at the older, solution-wide version.
- **`dotnet new classlib --framework` only accepts the cross-platform TFM values the template itself knows about** (e.g. `net10.0`, `netstandard2.x`) — it will reject an OS-specific TFM like `net9.0-windows` as an invalid template parameter even though the installed SDK can compile a project targeting it without issue once that TFM is written directly into the `.csproj`. Create the project without `--framework`, then hand-edit the generated `<TargetFramework>` value.
- **A global keyboard-to-action shortcut layer (for testing controller navigation without a physical gamepad) must check what actually has real focus before intercepting a keypress.** `MainWindow.OnKeyDown` previously translated mapped keys (Space, E, arrows, etc.) into `ControllerAction`s unconditionally; this was mostly harmless until Settings gained real per-key meaning (Space opening the virtual keyboard mid-typing, keystrokes not reaching a focused `TextBox`). Fixed by checking `FocusManager?.GetFocusedElement()` and skipping the shortcut translation entirely when it's a `TextBox`/`NumericUpDown`/`AutoCompleteBox`.
- **P/Invoke declarations must have an explicit, matching `CharSet` (and `EntryPoint` where the bare name isn't a real export) whenever a native struct field they touch is itself marshaled as Unicode.** `user32.dll` exports only `RegisterClassA`/`RegisterClassW` and `CreateWindowExA`/`CreateWindowExW` — no bare-named export exists. Without an explicit `CharSet`, `DllImport` defaults to Ansi and silently binds to the `...A` export; if the struct being passed (here, `WNDCLASS`) is itself marshaled as Unicode, the ANSI-interpreting function reads that Unicode pointer as ANSI bytes, corrupting the registered class name — `CreateWindowEx` then correctly fails to find a class by the real name (`ERROR_CANNOT_FIND_WND_CLASS`, 1407), which reads like a window-creation bug but is actually a class-registration corruption bug one layer up.
- **A settings-record reconstruction (`new AppSettings { ... }`) must be re-checked against the model's current full field list every time the model gains a new field.** `AudioConfigViewModel.SaveAsync()` was missing `EmulatorsRootPath`/`AddonsRootPath`/`LogsRootPath` and all four `CardHighlight*` fields — since `AppSettings` properties are mutable (`set`, not `init`) rather than `required`, this didn't fail to compile, it just silently reset those fields to their defaults on every Audio save. Any file that reconstructs a settings/config record this way is a standing risk whenever that record's shape changes elsewhere.
- **A conditional shortcut (e.g. "Left switches tabs, but only from position 0") is usually a bug, not a feature.** The Sounds tab's Left-to-return-to-Music behavior only fired from the very first field; everywhere else it was an explicit no-op "to avoid accidentally leaving the field grid" — which in practice just meant there was no way back once you'd moved past the first field. If a fallback path only works from one specific state, check whether that's actually intended before shipping it.
- **A "card looks like it's being skipped during navigation" symptom doesn't necessarily mean the navigation logic is wrong.** When Favorites appeared to get skipped while scrolling the Home Menu, the actual index/windowing math (traced by hand and later confirmed via temporary diagnostic logging) was correct in every single case — the real cause was a card whose placeholder letter (`Label[0]`, used as the giant background character when there's no cover art) happened to be an emoji, which apparently didn't render at the large placeholder font size, making a *correctly selected* card look empty. When a reported bug doesn't match what the traced data says is happening, look one layer up (rendering) rather than re-deriving the same math a fourth time. Fixed by finding the first actual letter/digit in the label instead of blindly using index 0.
- **When a mechanism can't be diagnosed from code alone after a couple of honest attempts, add temporary, specific diagnostic logging (index values, full list contents, before/after transitions) rather than keep guessing at the mechanism.** This is what actually resolved the Favorites-skip investigation above — the fix was obvious once the log showed the true state, whereas further reasoning about the existing code wasn't converging. Strip the diagnostic logging once the real cause is confirmed and fixed.
- **`ComboBox`'s `SelectedValue`/`SelectedValueBinding` combination has documented reliability problems in this Avalonia version** — confirmed via multiple upstream GitHub issues (unreliable behavior on programmatic changes, double-firing/null-then-correct-value on initialization, incompatibility with compiled bindings in some configurations). The Games editor's System/Emulator dropdowns used this pattern and silently failed to visually update when their selection was changed from code (`CycleSystem`/`CycleEmulator`). Fixed by switching to `SelectedItem` bound directly to a real object via a computed property, the same reliable pattern already used elsewhere (Audio's category-override combo) — prefer `SelectedItem` over `SelectedValue`+`SelectedValueBinding` for any ComboBox whose selection needs to change programmatically, not just via user interaction.
- **A full "Back always resets everything" convention (as used for the Categories/Systems tabs' list-vs-fields toggle) does not automatically generalize to every nested sub-mode.** When a sub-mode sits *inside* an already-open, not-yet-saved editor (the Games category checkbox grid, Audio's track sub-list), collapsing Back all the way out on the first press discards in-progress edits — the user needs a real step back to the editor's field list, not a full exit. `ConfigEditorViewModel.TryHandleContentBack()` (§6a.1) exists specifically for this: give the active tab a chance to close just its nested sub-mode, and only fall through to the full exit if there wasn't one open.
- **A stored path is only as portable as the code that reads it back.** Converting where a path is *saved* to relative form does nothing if the code that later *uses* that path doesn't resolve a relative value back to absolute first. Two real, latent bugs surfaced this way (§11c) — `HookLauncher.StartAsync` and `LibVlcAudioService.PlayCurrentTrack` both used a stored path completely raw, which was harmless only because nothing had ever stored a relative one yet. When adding relative-path support to a new field, audit the *read* side, not just the *write* side, before assuming it's safe.
- **A "reconstruction that rebuilds a model from scratch" bug (§13, earlier entries) recurs by default, not by exception, whenever a model gains a new field.** It happened a third time this session — `GamesConfigViewModel.CopyMediaFilesAsync` rebuilds a `Game` after copying media files, and `BezelOverridePath`/`BiosOverridePaths` were both silently dropped by that reconstruction until caught during the same round they were added. Any code that does `new SomeModel { ... }` from an existing instance rather than mutating in place is a standing liability against that model ever gaining another field — worth treating as a known smell, not a one-off mistake to catch and forget.
- **A web search confirming a technical format (e.g. an external tool's config file syntax) is real and current does not replace using it for something the model already "knows" without checking.** The RetroArch overlay `.cfg` format (`overlays = 1`, `overlay0_overlay = "..."`, `overlay0_full_screen = true`, `overlay0_descs = 0`) was verified against multiple independent sources before being used in `RetroArchConfigGenerator`, specifically because generating a wrong-but-plausible-looking config would fail silently (bezel just doesn't show) rather than loudly (compile error) — the kind of mistake that's expensive to debug later and cheap to avoid up front.
- **Windows PowerShell 5.1 (the default on most Windows installs, distinct from PowerShell 7+/`pwsh`) can misread a UTF-8 script file without a BOM, especially one containing non-ASCII characters (em-dashes, smart quotes) in comments — the corruption can manifest as a confusing, seemingly-unrelated parser error** ("Missing closing '}'" on a line that was never actually unbalanced) rather than an obvious encoding complaint. `build-release.ps1` hit this directly. The robust fix isn't chasing the exact encoding mismatch — it's avoiding non-ASCII characters in `.ps1` files entirely, since ASCII is identical under any encoding interpretation.
- **A brand-new git repo can pick up far more than intended the first time `git add .` runs, especially in a working directory that's accumulated scratch/backup folders over a long project's lifetime** (`_backup\`, old milestone `_zip files\`, stray duplicate downloads). A `.gitignore` written from general knowledge of what a .NET solution needs to exclude is not the same as one written after actually seeing what's sitting in the real working directory — worth treating the first `git status`/`git add .` on an established project as a checkpoint to actually inspect, not just a command to run.
- **Windows tags files extracted from a downloaded ZIP with a hidden "Mark of the Web" marker**, and `RemoteSigned`-or-stricter PowerShell execution policies block unsigned scripts carrying it regardless of where the file currently sits on disk — `Unblock-File` removes the marker and is the correct fix, not loosening the execution policy further.
- **When a symptom survives several plausible-sounding fixes in a row, stop guessing and add real diagnostics before trying another.** A reported "music doesn't auto-play at launch" bug survived three consecutive hypothesis-driven fixes (a LibVLC first-play race, then a suspected stale build-output config file, then a suspected reference-equality bug in the retry logic) before temporary `File.AppendAllText` diagnostic logging — bypassing the project's own removed logging infrastructure entirely — immediately showed the real cause in one exchange: a master-volume setting that was never applied at startup, only ever touched by the Settings tab's Save handler. Each earlier fix wasn't wrong to attempt, but continuing to reason about the same log-free symptom from code alone had stopped being productive.
- **Two separate "volume" concepts that both resolve to the same underlying native property will silently fight over which one sticks, and whichever code path runs more recently wins with no error.** `LibVlcAudioService.MusicVolume` (the master/Settings-tab volume) and `LoadAndPlayPlaylistAsync`'s per-playlist volume were both writing directly to `_musicPlayer.Volume` — setting the master volume at startup appeared to work, then got silently clobbered the moment a playlist loaded and wrote its own volume over it. The fix was storing the master value separately and computing an explicit `master × playlist` effective volume whenever a track loads, rather than letting either write directly to the player.
- **A property getter is not guaranteed to return the same object identity on repeated reads, even for the same underlying resource** — `LibVLCSharp.Shared.MediaPlayer.Media`'s getter constructs a new managed wrapper object each call. A retry-if-not-playing safety net that compared `ReferenceEquals(_musicPlayer.Media, expectedMedia)` to detect "has something newer taken over" silently never matched, so the retry never fired. A plain incrementing generation counter, compared by value, is the robust way to detect this instead of comparing wrapper objects that aren't guaranteed stable.
- **"Currently browsing/highlighted" and "actually entered" are not the same category context, and conflating them can ship silently.** `CurrentCategoryIdForPlaylistCycling(atHome: true)` returned the Home Menu's currently-*highlighted* category card (updated just by scrolling left/right) instead of `null`, letting a category-locked playlist become cyclable without ever actually entering that category's Game Browser. Only genuinely entering a category (`_gameBrowser.ActiveCategory`) should count as "in" it for this kind of category-scoped logic — being visually near it in a list should not.
- **A `<Content Include>` MSBuild rule intended purely as local-dev convenience (copying a repo-root folder into every build output) can have effects far outside its original intent once that output folder holds live, user-written runtime data** — it was already flagged once for risking personal data in a public release (§11f), and separately turned out to be silently resetting the *dev* build's own runtime config on rebuilds, which is what actually made a real audio bug look intermittent across testing sessions. Removed entirely rather than patched around, once its actual blast radius became clear.
- **A one-line "highlight moved, don't switch content yet" fix can be correct and still incomplete if a second, independent shortcut reaches the same effect.** Removing `OnSelectedMenuItemChanged`'s auto-`SelectTab` call was the right fix for mouse/gamepad *highlight* changes, but `MainWindowViewModel`'s `NavigateRight` case had its own separate `else _configEditor.EnterContent()` fallback — any rightward stick deflection (trivial to trigger while actually aiming Up/Down) still entered a tab with no confirm at all. Reported "stick drift" persisted across an entire fix-and-retest cycle because of this second path; the actual fix was `grep`-ing for every other call site of the method being guarded, not just the one already found.
- **A ComboBox/List-selection binding fix, a stick-input fix, and a status-message-wording fix can all look like the same underlying complaint from the user's side ("this still isn't working") when they're three unrelated bugs.** Diagonal-stick double-firing (Left/Right cycling scrape results also nudging focus to an adjacent button), a portable-path resolution gap (`File.Exists` on a raw relative path silently reading as "missing"), and a success message worded like an error ("no logo... scrape again") all independently produced "the feature seems broken" reports in the same session — each needed its own root cause, not one shared explanation.
- **A path-portability bug of the exact same shape (raw relative path passed straight to `File.Exists`/`File.OpenRead` without resolving via `PortablePathHelper.ToAbsolutePath` first) recurred three separate times in one feature** (Art-tab preview loading, the poster generator's reference-image fallback, and the logo-overlay check) before all three were caught — the fix is mechanically identical each time, but nothing currently prevents a fourth call site from reintroducing it. Worth treating "does this touch a `GameMedia`/`Editor.*Path` value" as a checklist item for "was `ToAbsolutePath` called" the same way `CopyMediaFilesAsync`'s field-completeness reconstruction bug (this section, above) is now a known-recurring smell.
- **An unquoted template token substituted into `ProcessStartInfo.Arguments` is a silent, title-specific failure, not a loud one.** `emulators.json`'s `Arguments: "{rom}"` (no quotes) worked for every ROM whose filename had no space, and silently launched the emulator to its own home menu — no error, no log entry — for the two titles that did (`Crazy Taxi 2 (USA).chd`, `NFL Blitz 2001 (USA).chd`), because Windows' command-line tokenizer splits unquoted `Arguments` on whitespace. Reads exactly like "these two games are somehow special" rather than "this argument-building code has a general gap" until the actual generated command line is inspected.
- **A repository method existing, being correctly implemented, and being called from the right place to *save* data is not evidence it's also called to *load* it back.** `PeripheralRegistry.SaveAsync()` had always worked — `controllers.json` genuinely held the user's scanned devices — but `LoadAsync()`, though fully implemented, had no call site anywhere in the app; `Program.cs` never invoked it at startup. `KnownDevices` silently started empty every session regardless of what was on disk, presenting identically to "never saves" even though saving was never the broken half.
- **An API field that looks optional/cosmetic (here, TheGamesDB's `boxart` `side: "front"|"back"`) can silently determine correctness when the code ignores it and just takes array order instead.** `TheGamesDbScraperSource` took whichever `boxart` entry the API happened to list first as the Cover — array order isn't a documented guarantee — so a back-cover-first response silently became the displayed Cover Art while the real front cover was discarded into alt-covers. Fixed by explicitly preferring `side == "front"`.

---

## 14. Development Milestones

| # | Milestone | Status |
|---|---|---|
| 1 | Solution architecture & DI bootstrapping | ✅ Complete |
| 2 | JSON data layer + 5-card dashboard shell | ✅ Complete |
| 3 | XInput controller + keyboard input service | ✅ Complete |
| 4 | Two-level navigation: HomeMenuViewModel + GameBrowserViewModel | ✅ Complete |
| 5 | Filter overlay (System, Genre, Players rows with pill navigation) | ✅ Complete |
| 6 | Media system (SkiaMediaCache, MediaAssetResolver, cover art loading) | ✅ Complete |
| 7 | Configuration editor (Games, Systems/Emulators, Audio tabs) | ✅ Complete |
| 8 | Audio system (LibVLCSharp background music, nav sounds, playlists) | ✅ Complete |
| 9 | Audio settings UI (Music tab + System Sounds tab with test buttons) | ✅ Complete |
| 10 | Emulator launcher (ProcessEmulatorLauncher, window minimize/restore) | ✅ Complete |
| 11 | Emulator config tab (Systems + Emulators sub-tabs, RetroArch toggle) | ✅ Complete |
| 12 | Video preview (LibVLCSharp.Avalonia VideoView, selected card only) | ✅ Complete |
| 13 | Theme engine (AvaloniaThemeService, DynamicResource, Theme tab) | ✅ Complete |
| 14 | Performance optimization + RetroArch Config Generator | ✅ Complete |
| 14b | Card media system overhaul (fill, live reload, direct assignment) | ✅ Complete |
| 14c | Paths tab (configurable root folders + per-system ROM paths) | ✅ Complete |
| 14d | Category art management (any filename, any drive, auto-copy on save) | ✅ Complete |
| 15 | RawInput hardware tracking + multi-device controller index | ✅ Complete |
| 15b | Full controller navigation across all Settings tabs (unified sidebar) | ✅ Complete |
| 15c | On-screen virtual keyboard (with cursor, insert-at-position editing) | ✅ Complete |
| 15d | Settings restructure (Categories own tab, Systems/Emulators relayout) | ✅ Complete |
| 15e | Right-stick scrolling | ✅ Complete |
| 15f | Multi-category game assignment | ✅ Complete |
| 15g | Card selection highlight (color wheel, intensity, style, width) | ✅ Complete |
| 15h | Remaining controller-nav gaps closed (Audio Music tab, Games combo/checkbox fields, LB/RB sub-tab switching) | ✅ Complete |
| 15i | Built-in Favorites category, alphabetical game sorting, Categories quick-add (X) | ✅ Complete |
| 16 | MameHooker / Hook of the Reaper integration (process lifecycle only, new UGL.Hooks project) | ✅ Complete |
| 17 | Final appliance-mode package testing & deployment | ✅ Complete — portable ZIP, self-contained single-file win-x64, `scripts\build-release.ps1`, first release (v0.1.0) published |
| 18 | BIOS files, bezel/overlay support (RetroArch native), full portable-path sweep, manual playlist switching | ✅ Complete |
| 19 | In-app update system (`UGL.Updates`, GitHub Releases) | ✅ Complete |
| 20 | New-user-friendly first-run experience (`START HERE.txt`, `media\README.txt`) | ✅ Complete |
| 21 | Playlist system rework — user-created playlists, independent Global/Category assignment, shared track library, drag-and-drop | ✅ Complete |
| 22 | Controller remap — Left Stick full navigation, D-Pad repurposed for music control | ✅ Complete |
| 23 | Per-game peripheral disable (Lightgun/Wheel/etc.) | ✅ Complete |
| 24 | First public releases (v0.1.0, v0.2.0) — update mechanism confirmed working in production | ✅ Complete |
| 25 | Card Art generation system — two generators (local resize + ComfyUI poster/collage), dynamic card-resolution targeting, persisted multi-screenshot asset, Games Settings/Art sub-tab restructure, app-wide delete confirmation, settings-menu gamepad-entry unification | ✅ Complete |

---

## 15. Acceptance Criteria

- Builds via `dotnet run --project src\UGL.App\UGL.App.csproj` with zero errors
- Controller-first UX — all navigation possible without keyboard or mouse, including every Settings tab and all text entry (via the on-screen keyboard)
- Stable 60 FPS during normal navigation
- All configuration persisted to JSON — no hard-coded values
- Category and game art assigned via Settings — no filename convention, any drive
- Card art updates immediately when Settings is closed — no restart required
- File system changes to art files reflect on cards within ~1 second (FileSystemWatcher)
- Themes switch instantly without restart
- Emulator launches, minimises UGL, restores focus on exit
- Per-system ROM paths support any drive or network share
- A game can belong to multiple categories and appears when browsing any of them
- The selected-card highlight (color, intensity, width, solid/pulsing) is user-configurable and applies identically on the Home Menu and Game Browser
- Modular, interface-driven architecture — all services replaceable via DI

---

## 16. Known Open Items

Carried forward from Milestone 15, not yet addressed:

- `UGL.App.csproj` may still have `<OutputType>Exe</OutputType>` from console-output debugging — should revert to `WinExe` before any release build.
- `[SETTINGS-NAV]`/`[KEYBOARD]` diagnostic logging (`MainWindowViewModel`) is verbose and was left in place through Milestone 15 for active debugging — worth stripping or gating behind a debug flag now that navigation is stable.
- The color wheel's exact hue-ring orientation relative to Avalonia's `ConicGradientBrush` convention was not independently verified (§13) — cosmetic only, does not affect picking correctness.
- The "no outer scroll, two-column field pairing" layout pattern applied to the Games tab (§9.7) has not yet been applied to the other config tabs (Systems, Audio, Categories, Theme, Card Highlight, Paths, Peripherals, Hooks, Updates, Scraper) — Games was the tab specifically reported as unusably cramped and was prioritized; the others still rely on the pre-existing outer `ScrollViewer` pattern.
- ScreenScraper and TheGamesDB scraper sources do not implement platform-filtered search (§9.7) — only IGDB does. A title shared across multiple platforms can return the wrong version's data from either of those two sources with no narrowing by the game's configured System.
- `MediaAssetResolver.ResolveScreenshot` (singular, pre-existing, distinct from the new `ResolveScreenshots` plural added in §9.7) has a copy-paste bug — it checks `game.Media.CoverPath` for non-empty but resolves and returns `game.Media.BackgroundPath` — flagged but not fixed this cycle (spun off as a separate follow-up task).
- **Fixed this cycle:** `AppFolderScaffolder` (and its generated `media\README.txt`, plus a stray reference in `PathsConfigView.axaml`) documented and created a nested `media\games\covers\` etc. layout that the actual save/load code (`GamesConfigViewModel.CopyMediaFileAsync`, `MediaAssetResolver`) never implemented — real art was always written flat (`media\covers\{systemId}-{gameId}.ext`), leaving `media\games\*` as permanently-empty decoy folders. §5 and the scaffolder now agree on the flat form; existing installs will have their old `media\games\*` folders removed on next launch cleanup or can be deleted by hand (they're always empty). Any game whose `Media.CoverPath`/`CardArtPath` still points into `media\_scrape_temp\` (i.e. was generated/scraped but never actually re-saved afterward) needs a Save in its editor to trigger the real copy into `media\covers\`/`media\cardart\` — this was never broken by the folder-structure bug itself, just easy to end up in via repeated test-generation without saving.
