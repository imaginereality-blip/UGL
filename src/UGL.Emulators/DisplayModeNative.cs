using System.Runtime.InteropServices;

namespace UGL.Emulators;

/// <summary>
/// Minimal P/Invoke surface for switching display resolution/refresh rate via
/// ChangeDisplaySettingsExW. Explicit CharSet.Unicode + the W-suffixed entry point
/// throughout — see the SDS §13 note on XInput/RawInput's Unicode/Ansi P/Invoke
/// lesson; the same silent-corruption risk applies here since DEVMODEW's string
/// fields are Unicode.
/// </summary>
internal static class DisplayModeNative
{
    private const string DllName = "user32.dll";

    [DllImport(DllName, EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    internal static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwFlags, IntPtr lParam);

    [DllImport(DllName, EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
    internal static extern int ChangeDisplaySettingsExToDefault(
        string? lpszDeviceName, IntPtr lpDevModeNull, IntPtr hwnd, uint dwFlags, IntPtr lParam);

    [DllImport(DllName, EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplaySettings(
        string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    internal const int ENUM_CURRENT_SETTINGS = -1;

    internal const int DM_PELSWIDTH       = 0x80000;
    internal const int DM_PELSHEIGHT      = 0x100000;
    internal const int DM_DISPLAYFREQUENCY = 0x400000;

    /// <summary>Temporary — reverts on logoff/reboot and doesn't touch the registry.</summary>
    internal const uint CDS_FULLSCREEN = 0x4;

    internal const int DISP_CHANGE_SUCCESSFUL = 0;
}

/// <summary>
/// DEVMODEW, sequential layout (not explicit offsets — those differ between the
/// Ansi and Unicode string marshaling, so let the marshaler compute them). Field
/// order and sizes verified against the current Microsoft Learn documentation for
/// DEVMODEW (wingdi.h) rather than assumed from memory.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DEVMODE
{
    private const int CCHDEVICENAME = 32;
    private const int CCHFORMNAME = 32;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
    public string dmDeviceName;
    public short dmSpecVersion;
    public short dmDriverVersion;
    public short dmSize;
    public short dmDriverExtra;
    public int dmFields;

    // Union: (dmOrientation/dmPaperSize/.../dmPrintQuality) for printers, or
    // (dmPosition.x, dmPosition.y, dmDisplayOrientation, dmDisplayFixedOutput) for
    // displays — same 16-byte footprint either way. Only the display variant matters
    // here, so it's laid out directly rather than modeled as a real union.
    public int dmPositionX;
    public int dmPositionY;
    public int dmDisplayOrientation;
    public int dmDisplayFixedOutput;

    public short dmColor;
    public short dmDuplex;
    public short dmYResolution;
    public short dmTTOption;
    public short dmCollate;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
    public string dmFormName;

    public short dmLogPixels;
    public int dmBitsPerPel;
    public int dmPelsWidth;
    public int dmPelsHeight;
    public int dmDisplayFlags; // union with dmNup — display-only usage here
    public int dmDisplayFrequency;
    public int dmICMMethod;
    public int dmICMIntent;
    public int dmMediaType;
    public int dmDitherType;
    public int dmReserved1;
    public int dmReserved2;
    public int dmPanningWidth;
    public int dmPanningHeight;
}
