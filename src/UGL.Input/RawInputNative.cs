using System.Runtime.InteropServices;

namespace UGL.Input;

/// <summary>
/// Win32 P/Invoke declarations for the RawInput API.
/// All structs use explicit LayoutKind.Sequential to match native memory layout.
/// </summary>
internal static class RawInputNative
{
    // ── Constants ──────────────────────────────────────────────────────────

    public const uint RIM_TYPE_MOUSE    = 0;
    public const uint RIM_TYPE_KEYBOARD = 1;
    public const uint RIM_TYPE_HID      = 2;

    public const uint RIDEV_INPUTSINK   = 0x00000100; // Receive input even when not in foreground
    public const uint RIDEV_DEVNOTIFY   = 0x00002000; // Receive WM_INPUT_DEVICE_CHANGE notifications
    public const uint RIDEV_REMOVE      = 0x00000001;

    public const uint RIDI_DEVICENAME   = 0x20000007;
    public const uint RIDI_DEVICEINFO   = 0x2000000B;

    public const uint RID_INPUT         = 0x10000003;

    // ── HID capability inspection (hid.dll) — the authoritative way to tell a real
    // controller input surface (has a recognized Usage Page/Usage AND actual buttons/axes)
    // apart from an auxiliary interface (dongle pairing channel, vendor telemetry, etc.)
    // that just happens to also show up as a RawInput HID device. ──────────────────────

    public const uint GENERIC_READ_NONE = 0; // query-only open; no read/write access needed
    public const uint FILE_SHARE_READ   = 0x00000001;
    public const uint FILE_SHARE_WRITE  = 0x00000002;
    public const uint OPEN_EXISTING     = 3;
    public static readonly nint INVALID_HANDLE_VALUE = -1;

    // HID_USAGE_PAGE_GENERIC, HID_USAGE_GENERIC_JOYSTICK, HID_USAGE_GENERIC_GAMEPAD are
    // already declared further below (used for RegisterRawInputDevices registration) —
    // reused here for capability checking. Only the multi-axis-controller usage is new.
    public const ushort HID_USAGE_GENERIC_MULTIAXIS_CONTROLLER = 0x08;

    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint hObject);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_GetPreparsedData(nint hidDeviceObject, out nint preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_FreePreparsedData(nint preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(nint preparsedData, out HIDP_CAPS capabilities);

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    // ── Device property lookup (cfgmgr32) — used to resolve real friendly names ──
    public const uint CR_SUCCESS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    // DEVPKEY_Device_FriendlyName: {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
    public static readonly DEVPROPKEY DEVPKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14,
    };

    // DEVPKEY_Device_DeviceDesc: {a45c254e-df1c-4efd-8020-67d146a850e0}, 2 (fallback if no friendly name is set)
    public static readonly DEVPROPKEY DEVPKEY_Device_DeviceDesc = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 2,
    };

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern uint CM_Get_Device_Interface_PropertyW(
        string pszDeviceInterface,
        ref DEVPROPKEY PropertyKey,
        out uint PropertyType,
        nint PropertyBuffer,
        ref uint PropertyBufferSize,
        uint ulFlags);

    // DEVPKEY_Device_InstanceId: {78c34fc8-104a-4aca-9ea4-524d52996e57}, 256
    // Links a device INTERFACE back to its owning device instance ID — needed because
    // FriendlyName/DeviceDesc live on the device instance (devnode), not the interface itself.
    public static readonly DEVPROPKEY DEVPKEY_Device_InstanceId = new()
    {
        fmtid = new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"),
        pid = 256,
    };

    public const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern uint CM_Locate_DevNodeW(
        out uint pdnDevInst,
        string pDeviceID,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern uint CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DEVPROPKEY PropertyKey,
        out uint PropertyType,
        nint PropertyBuffer,
        ref uint PropertyBufferSize,
        uint ulFlags);

    // Walks up the device tree — e.g. from a generic "HID-compliant game controller"
    // collection to the parent USB composite device or Bluetooth device that carries
    // the real product name (this is what Device Manager's "by connection" view shows).
    [DllImport("cfgmgr32.dll")]
    public static extern uint CM_Get_Parent(
        out uint pdnDevInstParent,
        uint dnDevInst,
        uint ulFlags);

    public const uint DEVPROP_TYPE_STRING = 0x00000012;

    // Resolves indirect string references (e.g. "@oem6.inf,%hid.devicedesc%;Fallback Text",
    // or "@driver.dll,-1234") into the real localized text — the same API Device Manager
    // uses internally. Without this, many FriendlyName/DeviceDesc values come back as raw
    // resource references instead of readable text.
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHLoadIndirectString(
        string pszSource,
        System.Text.StringBuilder pszOutBuf,
        int cchOutBuf,
        nint ppvReserved);

    public const uint WM_INPUT          = 0x00FF;
    public const uint WM_INPUT_DEVICE_CHANGE = 0x00FE;

    public const nint HWND_MESSAGE      = -3;

    public const uint MOUSE_MOVE_RELATIVE  = 0x0000;
    public const uint MOUSE_MOVE_ABSOLUTE  = 0x0001;

    public const uint RI_MOUSE_LEFT_BUTTON_DOWN   = 0x0001;
    public const uint RI_MOUSE_LEFT_BUTTON_UP     = 0x0002;
    public const uint RI_MOUSE_RIGHT_BUTTON_DOWN  = 0x0004;
    public const uint RI_MOUSE_RIGHT_BUTTON_UP    = 0x0008;
    public const uint RI_MOUSE_MIDDLE_BUTTON_DOWN = 0x0010;
    public const uint RI_MOUSE_MIDDLE_BUTTON_UP   = 0x0020;
    public const uint RI_MOUSE_WHEEL              = 0x0400;

    // ── Structs ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint   dwFlags;
        public nint   hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint  dwType;
        public uint  dwSize;
        public nint  hDevice;
        public nint  wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWMOUSE
    {
        public ushort usFlags;
        public uint   ulButtons;       // button flags + wheel delta packed
        public ushort usButtonFlags;
        public short  usButtonData;    // wheel delta when RI_MOUSE_WHEEL is set
        public uint   ulRawButtons;
        public int    lLastX;
        public int    lLastY;
        public uint   ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWHID
    {
        public uint dwSizeHid;
        public uint dwCount;
        // Variable-length raw HID data follows
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWINPUT_DATA
    {
        [FieldOffset(0)] public RAWMOUSE mouse;
        [FieldOffset(0)] public RAWHID   hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWINPUT_DATA  data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public nint  hDevice;
        public uint  dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_MOUSE
    {
        public uint dwId;
        public uint dwNumberOfButtons;
        public uint dwSampleRate;
        public bool fHasHorizontalWheel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO_HID
    {
        public uint dwVendorId;
        public uint dwProductId;
        public uint dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct RID_DEVICE_INFO
    {
        [FieldOffset(0)]  public uint dwSize;
        [FieldOffset(4)]  public uint dwType;
        [FieldOffset(8)]  public RID_DEVICE_INFO_MOUSE mouse;
        [FieldOffset(8)]  public RID_DEVICE_INFO_HID   hid;
    }

    // ── P/Invoke ───────────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]
        RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList(
        [Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    // CharSet.Unicode + explicit EntryPoint are required here: user32.dll exports only
    // GetRawInputDeviceInfoA and GetRawInputDeviceInfoW (no bare "GetRawInputDeviceInfo").
    // Without this, .NET's default CharSet.Ansi silently binds to the "A" (ANSI) export,
    // which returns single-byte text — but we read the result with PtrToStringUni (2 bytes
    // per char), corrupting every device path/name into garbage.
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetRawInputDeviceInfo(
        nint hDevice,
        uint uiCommand,
        nint pData,
        ref uint pcbSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(
        nint hRawInput,
        uint uiCommand,
        nint pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    public static extern nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref MSG lpMsg);

    // Same reasoning as GetRawInputDeviceInfoW above: WNDCLASS's lpszClassName is marshaled
    // as Unicode (see [StructLayout(CharSet = CharSet.Unicode)] below), but without an
    // explicit CharSet here this silently binds to the ANSI export RegisterClassA, which
    // reads that Unicode string pointer as if it were ANSI — corrupting the registered
    // class name so CreateWindowEx can never find it (error 1407, "Cannot find window class").
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassW", SetLastError = true)]
    public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("kernel32.dll")]
    public static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint   hwnd;
        public uint   message;
        public nint   wParam;
        public nint   lParam;
        public uint   time;
        public int    ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASS
    {
        public uint    style;
        public nint    lpfnWndProc;
        public int     cbClsExtra;
        public int     cbWndExtra;
        public nint    hInstance;
        public nint    hIcon;
        public nint    hCursor;
        public nint    hbrBackground;
        public string? lpszMenuName;
        public string  lpszClassName;
    }

    // HID Usage Pages and Usages for registration
    public const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    public const ushort HID_USAGE_GENERIC_MOUSE    = 0x02;
    public const ushort HID_USAGE_GENERIC_JOYSTICK = 0x04;
    public const ushort HID_USAGE_GENERIC_GAMEPAD  = 0x05;
    public const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;

    // Quit message for our message pump
    public const uint WM_UGL_QUIT = 0x8000 + 1;
}
