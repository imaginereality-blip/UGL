# UltimateGameLauncher (UGL)

A controller-first arcade cabinet game launcher for Windows, built with .NET 9 and Avalonia UI. Designed to run entirely from a portable folder — point it at your ROMs, emulators, and media, and it handles the rest: category/game browsing, RetroArch integration, BIOS management, bezels, background music, and more, all navigable with a controller alone.

## Features

- **Controller-first navigation** — every screen, including the full Settings UI, is fully navigable without a mouse or keyboard
- **Category and game browsing** with cover art, background art, logos, video previews, and marquees
- **Favorites** — automatically populated, built into the Home Menu, no manual setup required
- **RetroArch integration** — per-game override configs, run-ahead, shader presets, BIOS files, and native bezel/overlay support
- **Standalone emulator support** alongside RetroArch cores
- **BIOS file management** — configure per-emulator, with rare per-game overrides
- **Bezel/overlay support** for games that don't fill the screen (RetroArch native overlay system)
- **Background music** with per-category playlist overrides and on-the-fly manual playlist switching
- **Output hook integration** — MameHooker / Hook of the Reaper for light gun recoil/rumble and cabinet lighting
- **Fully portable** — every path the app stores is drive-letter independent; the whole install can move to a different drive or machine without breaking
- **In-app updates** — checks GitHub Releases for new versions, downloads and applies them in place

## Requirements

- Windows 10/11 (x64)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (for building from source — end users running a published release don't need this)

## Building from source

```powershell
git clone https://github.com/imaginereality-blip/UGL.git
cd UGL
dotnet build UGL.sln
dotnet run --project src\UGL.App
```

## Folder structure

UGL creates the following folders next to its executable on first run:

```
config\          Settings, category/game/system/emulator data (JSON)
media\
  categories\    Category card art
  games\
    covers\ video\ logos\ marquees\ screenshots\
  sounds\        Navigation/confirm/back/error sound effects
  music\         Background music tracks
  themes\        UI themes
roms\             Your ROM files, organized per-system
emulators\        Emulator executables/cores
bios\             BIOS files required by your emulators
bezels\           Bezel/overlay artwork
addons\           Optional tools (MameHooker, Hook of the Reaper, etc.)
retroarch\        Auto-generated RetroArch override configs (not user-edited)
logs\             Application logs
```

Every path UGL stores is relative to its own install folder whenever possible, so the whole thing can be moved to a different drive letter, or a different machine's USB port, without breaking.

## Project layout

| Project | Responsibility |
|---|---|
| `UGL.Core` | Domain models, interfaces — no dependencies on anything else in the solution |
| `UGL.App` | Avalonia UI, ViewModels, composition root |
| `UGL.Data` | JSON-backed repositories (games, categories, systems, emulators, audio playlists, hooks) |
| `UGL.Configuration` | App settings persistence |
| `UGL.Emulators` | Emulator/RetroArch process launching |
| `UGL.Media` | Image caching, audio playback (LibVLC) |
| `UGL.Themes` | UI theme engine |
| `UGL.Input` | Controller (XInput) and RawInput device handling |
| `UGL.Hooks` | MameHooker / Hook of the Reaper process lifecycle |
| `UGL.Updates` | GitHub Releases update checking and in-place update application |

## License

Licensed under the GNU General Public License v3.0 — see [LICENSE](LICENSE) for the full text.

## Contributing

This is currently a personal project under active development. Issues and pull requests are welcome, but expect the codebase to be evolving quickly.
