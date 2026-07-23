using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using UGL.Core.Interfaces;
using UGL.Core.Models;
using static UGL.Input.RawInputNative;

namespace UGL.Input;

/// <summary>
/// Win32 RawInput service. Creates a hidden HWND_MESSAGE window on a dedicated
/// background thread and registers for WM_INPUT from all HID mice and joysticks.
///
/// Resolves player indices from IPeripheralRegistry by hardware path.
/// Events are raised on the message pump thread — consumers handle dispatching.
///
/// Thread safety: Start/Stop are safe to call from any thread.
/// RawInputReceived is raised on the internal pump thread.
/// </summary>
public sealed class RawInputService : IRawInputService
{
    private readonly IPeripheralRegistry _registry;
    private readonly ILogger<RawInputService> _logger;

    public event EventHandler<RawInputEvent>? RawInputReceived;

    public bool IsRunning { get; private set; }

    // Empty by default (nothing disabled) — set by ProcessEmulatorLauncher right
    // before a game launches, cleared back to empty when it exits. Read from the
    // pump thread in ProcessRawInput, written from whatever thread launches/exits
    // a game — a plain HashSet reference swap (not mutated in place) is safe here
    // without extra locking, since ProcessRawInput only ever reads the reference.
    private volatile IReadOnlySet<RawInputDeviceType> _disabledDeviceTypes = new HashSet<RawInputDeviceType>();

    private Thread? _pumpThread;
    private nint    _messageHwnd;
    private volatile bool _stopRequested;

    // Delegate kept alive to prevent GC collection of the WndProc
    private WndProcDelegate? _wndProcDelegate;
    private delegate nint WndProcDelegate(nint hWnd, uint uMsg, nint wParam, nint lParam);

    private const string WindowClassName = "UGL_RawInput_MessageOnly";

    public RawInputService(IPeripheralRegistry registry, ILogger<RawInputService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public void Start()
    {
        if (IsRunning) return;

        _stopRequested = false;
        _pumpThread = new Thread(PumpLoop)
        {
            IsBackground = true,
            Name = "UGL.RawInput.Pump",
            Priority = ThreadPriority.AboveNormal,
        };
        _pumpThread.SetApartmentState(ApartmentState.STA);
        _pumpThread.Start();
        IsRunning = true;
        _logger.LogInformation("RawInput service started.");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _stopRequested = true;

        // Post quit to our message window
        if (_messageHwnd != 0)
            PostMessage(_messageHwnd, WM_UGL_QUIT, 0, 0);

        _pumpThread?.Join(2000);
        IsRunning = false;
        _logger.LogInformation("RawInput service stopped.");
    }

    public void SetDisabledDeviceTypes(IReadOnlySet<RawInputDeviceType> disabledTypes)
    {
        _disabledDeviceTypes = disabledTypes;
        _logger.LogInformation(
            "Disabled peripheral types set: {Types}",
            disabledTypes.Count == 0 ? "(none)" : string.Join(", ", disabledTypes));
    }

    public IReadOnlyList<RawInputDevice> EnumerateDevices()
    {
        var result = new List<RawInputDevice>();

        uint deviceCount = 0;
        GetRawInputDeviceList(null, ref deviceCount, (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>());
        if (deviceCount == 0) return result;

        var deviceList = new RAWINPUTDEVICELIST[deviceCount];
        if (GetRawInputDeviceList(deviceList, ref deviceCount,
                (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>()) == uint.MaxValue)
            return result;

        foreach (var entry in deviceList)
        {
            var device = BuildDeviceInfo(entry);
            if (device is not null && IsAssignablePeripheral(device.DeviceType))
                result.Add(device);
        }

        return result;
    }

    /// <summary>
    /// Only these device types make sense to assign a player index to. RawInput reports
    /// every HID device on the system — laptop sensors, converted PS/2 devices, monitor
    /// brightness controls, plain keyboards/mice, etc. — none of which belong in a
    /// per-player controller list, so they're filtered out here rather than shown to the user.
    /// </summary>
    private static bool IsAssignablePeripheral(RawInputDeviceType type) => type switch
    {
        RawInputDeviceType.Gamepad  => true,
        RawInputDeviceType.Lightgun => true,
        RawInputDeviceType.Wheel    => true,
        RawInputDeviceType.Spinner  => true,
        RawInputDeviceType.Trackball => true,
        _ => false, // excludes Unknown, Keyboard, and plain Mouse
    };

    // ── Message pump ───────────────────────────────────────────────────────

    private void PumpLoop()
    {
        // Register window class
        _wndProcDelegate = WndProc;
        var wndClass = new WNDCLASS
        {
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance     = GetModuleHandle(null),
            lpszClassName = WindowClassName,
        };
        var classAtom = RegisterClass(ref wndClass);
        if (classAtom == 0)
        {
            _logger.LogError("RegisterClass failed. LastError: {Err}", Marshal.GetLastWin32Error());
            return;
        }

        // Create hidden message-only window
        _messageHwnd = CreateWindowEx(
            0, WindowClassName, "UGL RawInput", 0,
            0, 0, 0, 0,
            HWND_MESSAGE, 0, GetModuleHandle(null), 0);

        if (_messageHwnd == 0)
        {
            _logger.LogError("Failed to create RawInput message window. LastError: {Err}",
                Marshal.GetLastWin32Error());
            return;
        }

        // Register for all mice and joystick/gamepad devices with INPUTSINK
        // so we receive input even when UGL is not the foreground window.
        var devices = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage     = HID_USAGE_GENERIC_MOUSE,
                dwFlags     = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                hwndTarget  = _messageHwnd,
            },
            new RAWINPUTDEVICE
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage     = HID_USAGE_GENERIC_JOYSTICK,
                dwFlags     = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                hwndTarget  = _messageHwnd,
            },
            new RAWINPUTDEVICE
            {
                usUsagePage = HID_USAGE_PAGE_GENERIC,
                usUsage     = HID_USAGE_GENERIC_GAMEPAD,
                dwFlags     = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                hwndTarget  = _messageHwnd,
            },
        };

        if (!RegisterRawInputDevices(devices, (uint)devices.Length,
                (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            _logger.LogError("RegisterRawInputDevices failed. LastError: {Err}",
                Marshal.GetLastWin32Error());
        }

        // Enumerate and merge devices into registry
        var connected = EnumerateDevices();
        _registry.MergeConnectedDevices(connected);
        _ = _registry.SaveAsync();

        _logger.LogInformation("RawInput: {Count} devices registered.", connected.Count);

        // Message loop
        while (!_stopRequested && GetMessage(out var msg, 0, 0, 0))
        {
            if (msg.message == WM_UGL_QUIT) break;
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_messageHwnd != 0)
        {
            DestroyWindow(_messageHwnd);
            _messageHwnd = 0;
        }
    }

    private nint WndProc(nint hWnd, uint uMsg, nint wParam, nint lParam)
    {
        if (uMsg == WM_INPUT)
        {
            ProcessRawInput(lParam);
            return 0;
        }

        if (uMsg == WM_INPUT_DEVICE_CHANGE)
        {
            // Device connected or disconnected — re-enumerate
            var connected = EnumerateDevices();
            _registry.MergeConnectedDevices(connected);
            _ = _registry.SaveAsync();
            return 0;
        }

        if (uMsg == WM_UGL_QUIT)
        {
            PostQuitMessage(0);
            return 0;
        }

        return DefWindowProc(hWnd, uMsg, wParam, lParam);
    }

    private unsafe void ProcessRawInput(nint lParam)
    {
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, 0, ref size, (uint)sizeof(RAWINPUTHEADER));
        if (size == 0) return;

        var buffer = stackalloc byte[(int)size];
        var ptr    = (nint)buffer;

        if (GetRawInputData(lParam, RID_INPUT, ptr, ref size,
                (uint)sizeof(RAWINPUTHEADER)) == uint.MaxValue)
            return;

        var raw = Marshal.PtrToStructure<RAWINPUT>(ptr);

        if (raw.header.dwType != RIM_TYPE_MOUSE && raw.header.dwType != RIM_TYPE_HID)
            return;

        var hardwarePath = GetHardwarePath(raw.header.hDevice);
        if (string.IsNullOrEmpty(hardwarePath)) return;

        var playerIndex = _registry.GetPlayerIndex(hardwarePath);
        var deviceType  = InferDeviceType(raw.header.hDevice, hardwarePath);

        RawInputEvent evt;

        if (raw.header.dwType == RIM_TYPE_MOUSE)
        {
            var mouse = raw.data.mouse;
            bool isAbsolute = (mouse.usFlags & MOUSE_MOVE_ABSOLUTE) != 0;
            short wheelDelta = (mouse.usButtonFlags & RI_MOUSE_WHEEL) != 0
                ? mouse.usButtonData : (short)0;

            uint pressed  = 0;
            uint released = 0;
            if ((mouse.usButtonFlags & RI_MOUSE_LEFT_BUTTON_DOWN)   != 0) pressed  |= 1;
            if ((mouse.usButtonFlags & RI_MOUSE_LEFT_BUTTON_UP)     != 0) released |= 1;
            if ((mouse.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_DOWN)  != 0) pressed  |= 2;
            if ((mouse.usButtonFlags & RI_MOUSE_RIGHT_BUTTON_UP)    != 0) released |= 2;
            if ((mouse.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) pressed  |= 4;
            if ((mouse.usButtonFlags & RI_MOUSE_MIDDLE_BUTTON_UP)   != 0) released |= 4;

            evt = new RawInputEvent
            {
                HardwarePath    = hardwarePath,
                PlayerIndex     = playerIndex,
                DeviceType      = deviceType,
                DeltaX          = isAbsolute ? 0 : mouse.lLastX,
                DeltaY          = isAbsolute ? 0 : mouse.lLastY,
                AbsoluteX       = isAbsolute ? mouse.lLastX : 0,
                AbsoluteY       = isAbsolute ? mouse.lLastY : 0,
                IsAbsolute      = isAbsolute,
                ButtonsPressed  = pressed,
                ButtonsReleased = released,
                WheelDelta      = wheelDelta,
            };
        }
        else
        {
            // HID joystick/gamepad — minimal pass-through for now
            // Full HID report parsing is per-device and handled in Milestone 16
            evt = new RawInputEvent
            {
                HardwarePath = hardwarePath,
                PlayerIndex  = playerIndex,
                DeviceType   = deviceType,
            };
        }

        // Silently drop input from any device type disabled for the currently
        // running game — the event never even reaches subscribers, rather than
        // relying on every consumer to remember to check this themselves.
        if (_disabledDeviceTypes.Contains(deviceType)) return;

        RawInputReceived?.Invoke(this, evt);
    }

    // ── Device info helpers ────────────────────────────────────────────────

    private static string GetHardwarePath(nint hDevice)
    {
        uint size = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, 0, ref size);
        if (size == 0) return string.Empty;

        var buffer = Marshal.AllocHGlobal((int)(size * 2));
        try
        {
            GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buffer, ref size);
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static RawInputDevice? BuildDeviceInfo(RAWINPUTDEVICELIST entry)
    {
        var path = GetHardwarePath(entry.hDevice);
        if (string.IsNullOrEmpty(path)) return null;

        RawInputDeviceType deviceType;

        if (entry.dwType == RIM_TYPE_MOUSE)
        {
            deviceType = InferTypeFromPath(path, RawInputDeviceType.Mouse);
        }
        else if (entry.dwType == RIM_TYPE_KEYBOARD)
        {
            deviceType = RawInputDeviceType.Keyboard;
        }
        else if (entry.dwType == RIM_TYPE_HID)
        {
            // The authoritative check: does this interface declare a recognized controller
            // Usage Page/Usage AND actually expose input buttons/axes? This is what separates
            // a real gamepad/wheel report interface from a dongle's pairing/config channel or
            // a vendor telemetry channel — those consistently fail one or both checks,
            // regardless of what generic name Windows happens to assign them.
            if (!TryGetHidCapabilities(path, out var caps) || !IsRealControllerSurface(caps))
                return null;

            deviceType = InferTypeFromPath(path, RawInputDeviceType.Gamepad);
        }
        else
        {
            return null;
        }

        return new RawInputDevice
        {
            HardwarePath  = path,
            FriendlyName  = GetFriendlyName(path),
            DeviceType    = deviceType,
            RuntimeHandle = entry.hDevice,
        };
    }

    /// <summary>
    /// Opens the device (query-only — no read/write access requested, so this works even
    /// if another process has the device open, and needs no elevated privileges) and reads
    /// its HID capabilities via the standard hid.dll preparsed-data API.
    /// </summary>
    private static bool TryGetHidCapabilities(string path, out HIDP_CAPS caps)
    {
        caps = default;

        var handle = CreateFile(path, GENERIC_READ_NONE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            0, OPEN_EXISTING, 0, 0);
        if (handle == 0 || handle == INVALID_HANDLE_VALUE) return false;

        try
        {
            if (!HidD_GetPreparsedData(handle, out var preparsedData) || preparsedData == 0)
                return false;

            try
            {
                return HidP_GetCaps(preparsedData, out caps) == HIDP_STATUS_SUCCESS;
            }
            finally
            {
                HidD_FreePreparsedData(preparsedData);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// A real controller input surface declares a Generic Desktop Joystick/Gamepad/
    /// Multi-axis-controller usage AND actually has input buttons or axes. Auxiliary
    /// interfaces (dongle pairing channels, vendor telemetry, LED/rumble-only output
    /// collections) consistently fail one or both of these regardless of naming.
    /// </summary>
    private static bool IsRealControllerSurface(HIDP_CAPS caps)
    {
        bool isControllerUsage = caps.UsagePage == HID_USAGE_PAGE_GENERIC &&
            (caps.Usage == HID_USAGE_GENERIC_JOYSTICK ||
             caps.Usage == HID_USAGE_GENERIC_GAMEPAD ||
             caps.Usage == HID_USAGE_GENERIC_MULTIAXIS_CONTROLLER);

        bool hasRealControls = caps.NumberInputButtonCaps > 0 || caps.NumberInputValueCaps > 0;

        return isControllerUsage && hasRealControls;
    }

    /// <summary>
    /// Fragments (checked case-insensitively as substrings) that indicate a name is one of
    /// Windows' generic driver-class labels rather than vendor-specific text. Substring
    /// matching is used instead of exact strings because the precise wording of these
    /// labels (especially the XInput ones) varies across Windows versions/locales —
    /// e.g. "Controller (XBOX 360 For Windows)" vs "Xbox 360 Controller for Windows".
    /// </summary>
    private static readonly string[] GenericNameFragments =
    [
        "hid-compliant",
        "usb input device",
        "usb composite device",
        "human interface device",
        "bluetooth le hid",
        "bluetooth low energy hid",
        // Microsoft's shared xusb.sys driver package uses XInput-generic wording for EVERY
        // XInput-compatible controller regardless of manufacturer (many third-party pads,
        // including 8BitDo, PowerA, etc. deliberately impersonate an Xbox 360 controller
        // for game compatibility) — this text carries no vendor information at all.
        "xbox 360",
        "xbox controller",
        "xinput",
        "generic usb joystick",
        // Climbing too far up the device tree can reach bus/hub infrastructure nodes —
        // never adopt one of these as if it were the actual peripheral's name.
        "usb hub",
        "root hub",
        "generic hub",
        "usb composite bus",
        "system bus",
    ];

    private static bool IsGenericName(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var fragment in GenericNameFragments)
        {
            if (lower.Contains(fragment)) return true;
        }
        return false;
    }

    /// <summary>
    /// USB Vendor IDs for known controller manufacturers. Used as a last-resort identifier
    /// when nothing in the device tree carries vendor-specific text — most commonly for
    /// controllers running in XInput compatibility mode (see GenericNameFragments above).
    /// </summary>
    private static readonly Dictionary<string, string> KnownVendorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2DC8"] = "8BitDo",
        ["057E"] = "Nintendo",
        ["054C"] = "Sony",
        ["045E"] = "Microsoft",
        ["046D"] = "Logitech",
        ["0E6F"] = "PDP",
        ["24C6"] = "PowerA",
        ["20D6"] = "PowerA",
        ["1038"] = "SteelSeries",
        ["1532"] = "Razer",
        ["0F0D"] = "Hori",
        ["3537"] = "GuliKit",
        ["28DE"] = "Valve",
        ["2717"] = "Xiaomi",
        ["18D1"] = "Google",
    };

    private const int MaxParentHops = 4;

    /// <summary>
    /// Resolves the Windows-assigned friendly name (or device description as fallback)
    /// for a device via its RawInput hardware path. FriendlyName/DeviceDesc are properties
    /// of the device INSTANCE (devnode), not the interface, so we first resolve the
    /// interface path to its owning device instance ID, then locate that devnode. If the
    /// name found there is one of Windows' generic HID/XInput labels, we climb up the
    /// device tree looking for a more specific product name on a parent node. If nothing
    /// in the tree carries vendor-specific text (common for XInput-mode controllers), we
    /// fall back to a known-vendor-ID lookup so at least the manufacturer is identifiable.
    /// </summary>
    private static string GetFriendlyName(string hardwarePath)
    {
        string? bestGenericFallback = null;

        var instanceId = TryGetInterfaceProperty(hardwarePath, DEVPKEY_Device_InstanceId);
        if (!string.IsNullOrEmpty(instanceId) &&
            CM_Locate_DevNodeW(out var devInst, instanceId, CM_LOCATE_DEVNODE_NORMAL) == CR_SUCCESS)
        {
            for (int hop = 0; hop < MaxParentHops; hop++)
            {
                var name = TryGetDevNodeProperty(devInst, DEVPKEY_Device_FriendlyName)
                        ?? TryGetDevNodeProperty(devInst, DEVPKEY_Device_DeviceDesc);

                if (name is not null)
                {
                    if (!IsGenericName(name))
                        return name; // found a real product name — stop here

                    bestGenericFallback ??= name;
                }

                if (CM_Get_Parent(out var parent, devInst, 0) != CR_SUCCESS)
                    break;

                devInst = parent;
            }
        }

        var vendor = TryGetKnownVendorName(hardwarePath);
        if (vendor is not null)
        {
            return bestGenericFallback is not null
                ? $"{vendor} Controller ({bestGenericFallback})"
                : $"{vendor} Controller";
        }

        return bestGenericFallback ?? ExtractFriendlyName(hardwarePath);
    }

    private static string? TryGetKnownVendorName(string hardwarePath)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            hardwarePath, "VID_([0-9A-Fa-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success && KnownVendorNames.TryGetValue(match.Groups[1].Value, out var vendor)
            ? vendor
            : null;
    }

    private static string? TryGetInterfaceProperty(string hardwarePath, DEVPROPKEY key)
    {
        try
        {
            uint size = 0;
            CM_Get_Device_Interface_PropertyW(hardwarePath, ref key, out _, 0, ref size, 0);
            if (size == 0) return null;

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                var result = CM_Get_Device_Interface_PropertyW(hardwarePath, ref key, out var propType, buffer, ref size, 0);
                if (result != CR_SUCCESS || propType != DEVPROP_TYPE_STRING) return null;

                var value = Marshal.PtrToStringUni(buffer)?.TrimEnd('\0');
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetDevNodeProperty(uint devInst, DEVPROPKEY key)
    {
        try
        {
            uint size = 0;
            CM_Get_DevNode_PropertyW(devInst, ref key, out _, 0, ref size, 0);
            if (size == 0) return null;

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                var result = CM_Get_DevNode_PropertyW(devInst, ref key, out var propType, buffer, ref size, 0);
                if (result != CR_SUCCESS || propType != DEVPROP_TYPE_STRING) return null;

                var name = Marshal.PtrToStringUni(buffer)?.TrimEnd('\0');
                return string.IsNullOrWhiteSpace(name) ? null : ResolveIndirectString(name);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Many drivers store friendly names as indirect resource string references
    /// (e.g. "@oem6.inf,%hid.devicedesc%;HID-compliant mouse" or "@driver.dll,-1234")
    /// rather than plain text. This resolves them the same way Device Manager does.
    /// </summary>
    private static string ResolveIndirectString(string raw)
    {
        if (!raw.StartsWith('@')) return raw;

        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int hr = SHLoadIndirectString(raw, sb, sb.Capacity, 0);
            return hr == 0 && sb.Length > 0 ? sb.ToString() : raw;
        }
        catch
        {
            return raw;
        }
    }

    private static RawInputDeviceType InferDeviceType(nint hDevice, string path)
    {
        uint size = (uint)Marshal.SizeOf<RID_DEVICE_INFO>();
        var infoPtr = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.WriteInt32(infoPtr, (int)size); // dwSize must be set before call
            GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, infoPtr, ref size);
            var info = Marshal.PtrToStructure<RID_DEVICE_INFO>(infoPtr);

            return info.dwType switch
            {
                RIM_TYPE_MOUSE    => InferTypeFromPath(path, RawInputDeviceType.Mouse),
                RIM_TYPE_KEYBOARD => RawInputDeviceType.Keyboard,
                _                 => InferTypeFromPath(path, RawInputDeviceType.Gamepad),
            };
        }
        finally { Marshal.FreeHGlobal(infoPtr); }
    }

    /// <summary>
    /// Heuristic type inference from device hardware path.
    /// Known VID/PIDs for popular arcade peripherals are checked first.
    /// Falls back to the provided default.
    /// </summary>
    private static RawInputDeviceType InferTypeFromPath(string path, RawInputDeviceType fallback)
    {
        var upper = path.ToUpperInvariant();

        // Sinden Lightgun: VID_16C0&PID_0F01 / 0F38
        if (upper.Contains("VID_16C0") && (upper.Contains("PID_0F01") || upper.Contains("PID_0F38")))
            return RawInputDeviceType.Lightgun;

        // Gun4IR: VID_2341 (Arduino)
        if (upper.Contains("VID_2341"))
            return RawInputDeviceType.Lightgun;

        // AimTrak: VID_D209
        if (upper.Contains("VID_D209"))
            return RawInputDeviceType.Lightgun;

        // Generic lightgun heuristic — "GUN" in path
        if (upper.Contains("GUN"))
            return RawInputDeviceType.Lightgun;

        return fallback;
    }

    private static string ExtractFriendlyName(string hardwarePath)
    {
        // Extract a human-readable portion from the hardware path
        // e.g. \\?\HID#VID_16C0&PID_0F01#... → VID_16C0&PID_0F01
        var parts = hardwarePath.Split('#', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : hardwarePath;
    }

    public void Dispose() => Stop();
}
