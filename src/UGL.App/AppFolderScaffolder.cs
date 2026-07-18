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
        Path.Combine("media", "games", "covers"),
        Path.Combine("media", "games", "video"),
        Path.Combine("media", "games", "logos"),
        Path.Combine("media", "games", "marquees"),
        Path.Combine("media", "games", "screenshots"),
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
    }

    private static void WriteReadmeIfMissing(string folder, string contents)
    {
        var path = Path.Combine(folder, "README.txt");
        if (File.Exists(path)) return;
        try { File.WriteAllText(path, contents); }
        catch { /* non-critical — a missing README never blocks startup */ }
    }
}
