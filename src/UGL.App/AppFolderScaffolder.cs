namespace UGL.App;

/// <summary>
/// Ensures the full portable folder layout documented in the SDS (§5) exists relative
/// to the exe, creating anything missing. Runs on every startup, not just "first run" —
/// Directory.CreateDirectory is a no-op when the folder already exists, so there's no
/// need for a separate first-run flag, and this also silently repairs the layout if the
/// user (or an installer/uninstaller in a future update) ever deletes a folder by hand.
/// </summary>
internal static class AppFolderScaffolder
{
    private static readonly string[] Folders =
    [
        "config",
        Path.Combine("media", "categories"),
        // Flat, not media\games\* — must match the actual subfolder names
        // CopyMediaFileAsync/MediaAssetResolver use (GamesConfigViewModel.cs /
        // MediaAssetResolver.cs). An earlier version of this scaffolder (and its
        // README below) documented a media\games\covers\ nested convention that the
        // save/load code never actually implemented, leaving media\games\* as
        // permanently-empty decoy folders while real art silently landed flat.
        Path.Combine("media", "covers"),
        Path.Combine("media", "backgrounds"),
        Path.Combine("media", "logos"),
        Path.Combine("media", "marquees"),
        Path.Combine("media", "cardart"),
        Path.Combine("media", "screenshots"),
        Path.Combine("media", "video"),
        Path.Combine("media", "sounds"),
        Path.Combine("media", "music"),
        Path.Combine("media", "themes"),
        "roms",
        "emulators",
        "bios",
        "bezels",
        "addons",
        "retroarch",
        "logs",
    ];

    public static void EnsureFolders()
    {
        var root = AppContext.BaseDirectory;

        foreach (var relative in Folders)
            Directory.CreateDirectory(Path.Combine(root, relative));

        // Small, only-if-missing pointers in the folders a user actually has to
        // populate themselves — never overwritten once they exist, so nothing here
        // clobbers anything the user has already put there.
        WriteReadmeIfMissing(Path.Combine(root, "roms"),
            "Put your ROM files here, organized in one sub-folder per system.\r\n" +
            "The sub-folder name should match the System Id you configure in\r\n" +
            "Settings -> Systems (for example: roms\\nes\\, roms\\snes\\, roms\\mame\\).\r\n" +
            "You can also point a system at a different location entirely via its\r\n" +
            "per-system ROM path override in Settings -> Systems.");

        WriteReadmeIfMissing(Path.Combine(root, "emulators"),
            "Put emulator executables/cores here, or point directly at an\r\n" +
            "existing install elsewhere via the Executable Path field when you\r\n" +
            "add or edit an emulator in Settings -> Systems -> Emulators.");

        WriteReadmeIfMissing(Path.Combine(root, "addons"),
            "Optional third-party tools go here, for example MameHooker or\r\n" +
            "Hook of the Reaper for light gun recoil/rumble and cabinet lighting.\r\n" +
            "Point Settings -> Output Hooks at whichever one you've installed.");

        WriteReadmeIfMissing(Path.Combine(root, "bios"),
            "Put required BIOS files here, flat (no per-system sub-folders) —\r\n" +
            "this matches the convention most emulators and RetroArch cores\r\n" +
            "already expect for their own \"system directory\" setting, so if you\r\n" +
            "point RetroArch's System Directory at this same folder, both share\r\n" +
            "the same set of files. Configure which BIOS file(s) an emulator\r\n" +
            "needs in Settings -> Systems -> Emulators; individual games can\r\n" +
            "override this in rare cases where a specific game needs something\r\n" +
            "different from its emulator's default.");

        WriteReadmeIfMissing(Path.Combine(root, "bezels"),
            "Put bezel/overlay artwork here — images that fill the space\r\n" +
            "around a game's picture when it doesn't fill the whole screen\r\n" +
            "(for example, a 4:3 game on a 16:9 display). Configure a bezel\r\n" +
            "per system (as a default) or per game (as an override) in\r\n" +
            "Settings.");

        WriteReadmeIfMissing(Path.Combine(root, "media"),
            "This folder holds all the artwork, video, and audio UGL displays\r\n" +
            "and plays. You don't usually need to drop files in here directly —\r\n" +
            "when you Browse to an image/video/sound file anywhere in Settings,\r\n" +
            "UGL copies it into the right spot below automatically, named\r\n" +
            "{systemId}-{gameId}.{ext} (games) or by whatever filename you gave it\r\n" +
            "(categories).\r\n" +
            "\r\n" +
            "  categories\\           Category card art (Home Menu)\r\n" +
            "  covers\\               Game cover art\r\n" +
            "  backgrounds\\          Game background art (shown behind the card grid)\r\n" +
            "  logos\\                Game logo/wheel art\r\n" +
            "  marquees\\             Game marquee art\r\n" +
            "  cardart\\              Generated/cleaned-up card art (Games -> Art tab)\r\n" +
            "  screenshots\\          Game screenshots (up to 3 per game)\r\n" +
            "  video\\                Game preview video, plays when a game is selected\r\n" +
            "  sounds\\               Navigate/confirm/back/error sound effects\r\n" +
            "  music\\                Background music tracks (Settings -> Audio)\r\n" +
            "  themes\\               UI themes\r\n" +
            "\r\n" +
            "Note: these are all flat folders (no per-game subfolder) — a game's own\r\n" +
            "identity is baked into its filename instead.");

        // A single, ordered overview at the app's own root — not tucked inside any
        // one folder — since a first-time user extracting the portable ZIP sees this
        // before they've even opened any sub-folder, let alone the app itself.
        WriteReadmeIfMissing(root, GettingStartedText, fileName: "START HERE.txt");
    }

    private const string GettingStartedText =
        "Welcome to UltimateGameLauncher (UGL)!\r\n" +
        "\r\n" +
        "This is a portable app — everything it needs lives in the folders\r\n" +
        "right next to this file. Here's the recommended order to set it up:\r\n" +
        "\r\n" +
        "1. EMULATORS\r\n" +
        "   Put your emulator(s) in the emulators\\ folder (or note where they\r\n" +
        "   already are — they don't have to move). See emulators\\README.txt.\r\n" +
        "\r\n" +
        "2. ROMS\r\n" +
        "   Put your ROM files in roms\\, one sub-folder per system\r\n" +
        "   (e.g. roms\\nes\\, roms\\snes\\). See roms\\README.txt.\r\n" +
        "\r\n" +
        "3. BIOS FILES (if any of your systems need them)\r\n" +
        "   Put them in bios\\. See bios\\README.txt.\r\n" +
        "\r\n" +
        "4. RUN UGL.exe AND OPEN SETTINGS\r\n" +
        "   Press Start on a controller (or click the gear icon) to open\r\n" +
        "   Settings. Add your systems and emulators under Settings -> Systems,\r\n" +
        "   pointing at the ROM/emulator/BIOS locations from steps 1-3.\r\n" +
        "\r\n" +
        "5. ADD GAMES\r\n" +
        "   Under Settings -> Games, add each game and Browse to its ROM file\r\n" +
        "   and cover art — UGL organizes the art into media\\ automatically.\r\n" +
        "\r\n" +
        "6. OPTIONAL EXTRAS\r\n" +
        "   - Category art:      Settings -> Categories\r\n" +
        "   - Background music:  Settings -> Audio\r\n" +
        "   - Bezels:             Settings -> Systems (per system) or per game\r\n" +
        "   - Light gun/lighting: Settings -> Output Hooks (see addons\\README.txt)\r\n" +
        "\r\n" +
        "That's it — everything else (favorites, playlists, updates) is\r\n" +
        "discoverable from inside Settings once you're up and running.\r\n";

    private static void WriteReadmeIfMissing(string folder, string contents, string fileName = "README.txt")
    {
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path)) return;
        try { File.WriteAllText(path, contents); }
        catch { /* non-critical — a missing README never blocks startup */ }
    }
}
