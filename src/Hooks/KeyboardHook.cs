// src/Hooks/KeyboardHook.cs
using System.Runtime.InteropServices;
using RealTranslate.Core;
using RealTranslate.Native;

namespace RealTranslate.Hooks;

/// <summary>
/// Global low-level keyboard hook that captures all keyboard events
/// and sends them to the BufferManager via the KeyboardEvents channel.
/// </summary>
public class KeyboardHook : IDisposable
{
    private readonly AppState _appState;
    private readonly System.Threading.Channels.ChannelWriter<KbEvent> _eventWriter;
    private readonly object _lock = new();

    private IntPtr _hookHandle = IntPtr.Zero;
    private Win32.LowLevelKeyboardProc? _hookProc; // Keep delegate alive
    private Thread? _hookThread;
    private volatile bool _isRunning;

    // Virtual Key Codes for common keys
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12; // Alt
    private const ushort VK_CAPITAL = 0x14; // Caps Lock
    private const ushort VK_NUMLOCK = 0x90;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;

    // Punctuation VK codes (0x30-0x39 are 0-9, 0x41-0x5A are A-Z)
    private const ushort VK_OEM_PERIOD = 0xBE;      // .
    private const ushort VK_OEM_COMMA = 0xBC;       // ,
    private const ushort VK_OEM_MINUS = 0xBD;       // -
    private const ushort VK_OEM_PLUS = 0xBB;        // =
    private const ushort VK_OEM_1 = 0xBA;           // ; :
    private const ushort VK_OEM_2 = 0xBF;           // / ?
    private const ushort VK_OEM_3 = 0xC0;           // ` ~
    private const ushort VK_OEM_4 = 0xDB;           // [ {
    private const ushort VK_OEM_5 = 0xDC;           // \ |
    private const ushort VK_OEM_6 = 0xDD;           // ] }
    private const ushort VK_OEM_7 = 0xDE;           // ' "
    private const ushort VK_OEM_102 = 0xE2;         // <> or \|

    // Message pump constants
    private const int WM_QUIT = 0x0012;

    // Accessibility GUID for IAccessible (copy to avoid ref issues)
    private static readonly Guid IID_IAccessible_Local = new(
        0x618736E0, 0x3C3D, 0x11CF, 0x81, 0x0C, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    // Accessibility constants
    private const uint OBJID_CLIENT = 0xFFFFFFFC;
    private const int ROLE_SYSTEM_TEXT = 0x2A;
    private const int STATE_SYSTEM_PROTECTED = 0x10;

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostThreadMessage(int idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("oleacc.dll", SetLastError = true)]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd, uint dwObjectID, [In] ref Guid riid, out IntPtr pacc);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public KeyboardHook(AppState appState)
    {
        _appState = appState ?? throw new ArgumentNullException(nameof(appState));
        _eventWriter = AppChannels.KeyboardEvents.Writer;
    }

    /// <summary>
    /// Starts the keyboard hook on a dedicated thread with a Windows message pump.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning)
                return;

            _isRunning = true;

            _hookThread = new Thread(HookThreadProc)
            {
                Name = "KeyboardHookThread",
                IsBackground = true
            };

            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }
    }

    /// <summary>
    /// Stops the keyboard hook and cleans up resources.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            // Post WM_QUIT to the hook thread to exit the message loop
            if (_hookThread != null && _hookThread.IsAlive)
            {
                PostThreadMessage(_hookThread.ManagedThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _hookThread.Join(TimeSpan.FromSeconds(2));
            }

            Unhook();
        }
    }

    private void HookThreadProc()
    {
        // Create the hook delegate and keep it alive
        _hookProc = LowLevelKeyboardProc;

        // Set up the low-level keyboard hook
        IntPtr hModule = Win32.GetModuleHandle(null);
        _hookHandle = Win32.SetWindowsHookEx(
            Win32.WH_KEYBOARD_LL,
            _hookProc,
            hModule,
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to set keyboard hook. Error: {error}");
        }

        // Run message pump - required for low-level hooks to work
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // Clean up hook when message loop exits
        Unhook();
    }

    private IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Must call CallNextHookEx if nCode < 0
        if (nCode < 0)
            return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // Only process key down events
        if (wParam != (IntPtr)Win32.WM_KEYDOWN && wParam != (IntPtr)Win32.WM_SYSKEYDOWN)
            return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // Skip events if we're injecting or paused
        if (_appState.IsInjecting || _appState.IsPaused)
            return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // Get the virtual key code
        int vkCode = Marshal.ReadInt32(lParam);
        ushort vk = (ushort)vkCode;

        // Check for modifier key states
        bool ctrlPressed = (NativeMethods.GetKeyState(VK_CONTROL) & 0x8000) != 0;
        bool altPressed = (NativeMethods.GetKeyState(VK_MENU) & 0x8000) != 0;

        // Ignore Ctrl/Alt combinations (shortcuts)
        if (ctrlPressed || altPressed)
            return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // Ignore standalone modifier keys
        if (IsModifierKey(vk))
            return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // Ignore function keys and other special keys
        if (IsIgnoredKey(vk))
            return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // Get the foreground window handle
        IntPtr windowHandle = Win32.GetForegroundWindow();

        // Check for password field
        bool isPasswordField = IsPasswordField(windowHandle);

        // Convert VK to KbEvent
        KbEvent? kbEvent = ConvertToKbEvent(vk, windowHandle, isPasswordField);

        if (kbEvent != null)
        {
            // Send to channel (non-blocking)
            _eventWriter.TryWrite(kbEvent);
        }

        return Win32.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(ushort vk)
    {
        return vk switch
        {
            VK_SHIFT or VK_LSHIFT or VK_RSHIFT => true,
            VK_CONTROL or VK_LCONTROL or VK_RCONTROL => true,
            VK_MENU or VK_LMENU or VK_RMENU => true,
            VK_CAPITAL => true,
            VK_NUMLOCK => true,
            _ => false
        };
    }

    private static bool IsIgnoredKey(ushort vk)
    {
        // Function keys (F1-F24)
        if (vk >= 0x70 && vk <= 0x87)
            return true;

        // Navigation keys
        if (vk >= 0x21 && vk <= 0x28) // Page Up, Page Down, End, Home, Left, Up, Right, Down
            return true;

        // Insert, Delete
        if (vk == 0x2D || vk == 0x2E)
            return true;

        // Escape, Tab (we might want Tab later, but skip for now)
        if (vk == 0x1B || vk == 0x09)
            return true;

        return false;
    }

    private static KbEvent? ConvertToKbEvent(ushort vk, IntPtr windowHandle, bool isPasswordField)
    {
        KbEventType eventType;
        char? character = null;

        switch (vk)
        {
            case Win32.VK_SPACE:
                eventType = KbEventType.Space;
                break;

            case Win32.VK_BACK:
                eventType = KbEventType.Backspace;
                break;

            case Win32.VK_RETURN:
                eventType = KbEventType.Enter;
                break;

            // Punctuation keys
            case VK_OEM_PERIOD:
            case VK_OEM_COMMA:
            case VK_OEM_MINUS:
            case VK_OEM_PLUS:
            case VK_OEM_1:
            case VK_OEM_2:
            case VK_OEM_3:
            case VK_OEM_4:
            case VK_OEM_5:
            case VK_OEM_6:
            case VK_OEM_7:
            case VK_OEM_102:
                eventType = KbEventType.Punctuation;
                character = VkToChar(vk);
                break;

            // Alphanumeric keys (0-9, A-Z)
            default:
                if (vk >= 0x30 && vk <= 0x39) // 0-9
                {
                    eventType = KbEventType.Char;
                    character = (char)vk;
                }
                else if (vk >= 0x41 && vk <= 0x5A) // A-Z
                {
                    eventType = KbEventType.Char;
                    // Convert to lowercase (actual case depends on Shift state)
                    character = (char)(vk + 32); // Convert to lowercase
                }
                else
                {
                    // Unknown key, skip it
                    return null;
                }
                break;
        }

        return new KbEvent(character, eventType, windowHandle, isPasswordField);
    }

    private static char VkToChar(ushort vk)
    {
        // Simplified mapping for punctuation (without shift consideration)
        return vk switch
        {
            VK_OEM_PERIOD => '.',
            VK_OEM_COMMA => ',',
            VK_OEM_MINUS => '-',
            VK_OEM_PLUS => '=',
            VK_OEM_1 => ';',
            VK_OEM_2 => '/',
            VK_OEM_3 => '`',
            VK_OEM_4 => '[',
            VK_OEM_5 => '\\',
            VK_OEM_6 => ']',
            VK_OEM_7 => '\'',
            VK_OEM_102 => '\\',
            _ => '?'
        };
    }

    /// <summary>
    /// Checks if the focused control in the specified window is a password field
    /// using the Windows Accessibility API.
    /// </summary>
    public bool IsPasswordField(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return false;

        try
        {
            IntPtr pAcc = IntPtr.Zero;
            Guid iid = IID_IAccessible_Local;
            int hr = AccessibleObjectFromWindow(
                windowHandle,
                OBJID_CLIENT,
                ref iid,
                out pAcc);

            if (hr != 0 || pAcc == IntPtr.Zero)
                return CheckWindowTextForPassword(windowHandle);

            try
            {
                // Query for IAccessible interface via COM
                object? obj = Marshal.GetObjectForIUnknown(pAcc);
                if (obj == null)
                    return CheckWindowTextForPassword(windowHandle);

                // Use dynamic to access accessibility properties
                dynamic accessible = obj;
                try
                {
                    // Check the state of the accessible object
                    // Password fields have STATE_SYSTEM_PROTECTED (0x10)
                    object state = accessible.accState;
                    if (state is int stateInt && (stateInt & STATE_SYSTEM_PROTECTED) != 0)
                        return true;
                }
                catch (Exception)
                {
                    // accState might not be available
                }

                return CheckWindowTextForPassword(windowHandle);
            }
            finally
            {
                Marshal.Release(pAcc);
            }
        }
        catch (Exception)
        {
            // If accessibility API fails, try alternative method
            return CheckWindowTextForPassword(windowHandle);
        }
    }

    private static bool CheckWindowTextForPassword(IntPtr windowHandle)
    {
        // Get window class name
        Span<char> className = stackalloc char[256];
        int len = NativeMethods.GetClassName(windowHandle, className);

        if (len > 0)
        {
            string classNameStr = className.Slice(0, len).ToString().ToLowerInvariant();

            // Check for common password control class names
            if (classNameStr.Contains("password"))
                return true;

            // Check for edit control with ES_PASSWORD style
            if (classNameStr.Contains("edit") && HasPasswordStyle(windowHandle))
                return true;
        }

        return false;
    }

    private static bool HasPasswordStyle(IntPtr windowHandle)
    {
        // Get window style
        IntPtr style = NativeMethods.GetWindowLongPtr(windowHandle, -16); // GWL_STYLE

        // ES_PASSWORD = 0x0020
        const long ES_PASSWORD = 0x0020;
        return ((long)style & ES_PASSWORD) != 0;
    }

    private void Unhook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _hookProc = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    ~KeyboardHook()
    {
        Dispose();
    }
}

/// <summary>
/// Additional P/Invoke methods needed for keyboard hook
/// </summary>
internal static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, Span<char> lpClassName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
}
