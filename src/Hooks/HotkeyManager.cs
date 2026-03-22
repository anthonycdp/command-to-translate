// src/Hooks/HotkeyManager.cs
using System.Runtime.InteropServices;
using RealTranslate.Core;
using RealTranslate.Native;

namespace RealTranslate.Hooks;

/// <summary>
/// Manages global hotkey registration for toggling translation on/off.
/// Uses the RegisterHotKey Win32 API for system-wide hotkey detection.
/// </summary>
public class HotkeyManager : IDisposable
{
    private readonly AppState _appState;
    private readonly IntPtr _windowHandle;
    private readonly int _hotkeyId;
    private bool _isRegistered;
    private bool _disposed;

    /// <summary>
    /// Event raised when the registered hotkey is pressed.
    /// </summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>
    /// Creates a new HotkeyManager instance.
    /// </summary>
    /// <param name="appState">The application state to toggle.</param>
    /// <param name="windowHandle">The window handle to register the hotkey with.</param>
    /// <param name="hotkeyId">Unique identifier for the hotkey (default: 1).</param>
    public HotkeyManager(AppState appState, IntPtr windowHandle, int hotkeyId = 1)
    {
        _appState = appState ?? throw new ArgumentNullException(nameof(appState));
        _windowHandle = windowHandle;
        _hotkeyId = hotkeyId;
    }

    /// <summary>
    /// Registers the hotkey with Windows.
    /// </summary>
    /// <returns>True if registration was successful, false otherwise.</returns>
    public bool Register()
    {
        if (_isRegistered)
            return true;

        var hotkeyConfig = _appState.Config?.Hotkey;
        if (hotkeyConfig == null)
            return false;

        uint modifiers = ParseModifiers(hotkeyConfig.Modifiers);
        uint key = ParseKey(hotkeyConfig.Key);

        if (key == 0)
            return false;

        bool success = Win32.RegisterHotKey(_windowHandle, _hotkeyId, modifiers, key);

        if (success)
        {
            _isRegistered = true;
        }
        else
        {
            int error = Marshal.GetLastWin32Error();
            // Hotkey registration failed - could be already registered by another app
        }

        return success;
    }

    /// <summary>
    /// Processes a Windows message to check for hotkey press.
    /// Should be called from the window's WndProc or message loop.
    /// </summary>
    /// <param name="msg">The message ID.</param>
    /// <param name="wParam">The wParam of the message.</param>
    /// <returns>True if the message was a hotkey press and was handled.</returns>
    public bool ProcessMessage(int msg, IntPtr wParam)
    {
        if (msg != Win32.WM_HOTKEY)
            return false;

        // Check if this is our hotkey
        int hotkeyId = wParam.ToInt32();
        if (hotkeyId != _hotkeyId)
            return false;

        // Toggle the paused state
        _appState.IsPaused = !_appState.IsPaused;

        // Raise the event
        HotkeyPressed?.Invoke(this, EventArgs.Empty);

        return true;
    }

    /// <summary>
    /// Parses a list of modifier names into a Win32 modifier flags value.
    /// </summary>
    /// <param name="modifiers">List of modifier names (Ctrl, Shift, Alt, Win).</param>
    /// <returns>Combined modifier flags.</returns>
    public static uint ParseModifiers(List<string>? modifiers)
    {
        uint result = 0;

        if (modifiers == null)
            return result;

        foreach (string modifier in modifiers)
        {
            result |= modifier.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => Win32.MOD_CONTROL,
                "SHIFT" => Win32.MOD_SHIFT,
                "ALT" => Win32.MOD_ALT,
                "WIN" or "WINDOWS" => Win32.MOD_WIN,
                _ => 0
            };
        }

        return result;
    }

    /// <summary>
    /// Parses a key name into a virtual key code.
    /// </summary>
    /// <param name="key">Key name (e.g., "T", "F1", "Space", "Enter").</param>
    /// <returns>Virtual key code, or 0 if invalid.</returns>
    public static uint ParseKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return 0;

        string normalizedKey = key.ToUpperInvariant().Trim();

        // Single character keys (A-Z, 0-9)
        if (normalizedKey.Length == 1)
        {
            char c = normalizedKey[0];
            if (c >= 'A' && c <= 'Z')
                return c; // VK codes for A-Z are the same as ASCII
            if (c >= '0' && c <= '9')
                return c; // VK codes for 0-9 are the same as ASCII
        }

        // Named keys
        return normalizedKey switch
        {
            "SPACE" => 0x20,        // VK_SPACE
            "ENTER" or "RETURN" => 0x0D, // VK_RETURN
            "TAB" => 0x09,          // VK_TAB
            "ESCAPE" or "ESC" => 0x1B, // VK_ESCAPE
            "BACKSPACE" or "BACK" => 0x08, // VK_BACK
            "INSERT" => 0x2D,       // VK_INSERT
            "DELETE" => 0x2E,       // VK_DELETE
            "HOME" => 0x24,         // VK_HOME
            "END" => 0x23,          // VK_END
            "PAGEUP" => 0x21,       // VK_PRIOR
            "PAGEDOWN" => 0x22,     // VK_NEXT
            "UP" => 0x26,           // VK_UP
            "DOWN" => 0x28,         // VK_DOWN
            "LEFT" => 0x25,         // VK_LEFT
            "RIGHT" => 0x27,        // VK_RIGHT

            // Function keys F1-F24
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "F13" => 0x7C,
            "F14" => 0x7D,
            "F15" => 0x7E,
            "F16" => 0x7F,
            "F17" => 0x80,
            "F18" => 0x81,
            "F19" => 0x82,
            "F20" => 0x83,
            "F21" => 0x84,
            "F22" => 0x85,
            "F23" => 0x86,
            "F24" => 0x87,

            // Numpad keys
            "NUMPAD0" => 0x60,
            "NUMPAD1" => 0x61,
            "NUMPAD2" => 0x62,
            "NUMPAD3" => 0x63,
            "NUMPAD4" => 0x64,
            "NUMPAD5" => 0x65,
            "NUMPAD6" => 0x66,
            "NUMPAD7" => 0x67,
            "NUMPAD8" => 0x68,
            "NUMPAD9" => 0x69,
            "MULTIPLY" => 0x6A,
            "ADD" => 0x6B,
            "SEPARATOR" => 0x6C,
            "SUBTRACT" => 0x6D,
            "DECIMAL" => 0x6E,
            "DIVIDE" => 0x6F,

            _ => 0
        };
    }

    /// <summary>
    /// Unregisters the hotkey.
    /// </summary>
    public void Unregister()
    {
        if (_isRegistered)
        {
            Win32.UnregisterHotKey(_windowHandle, _hotkeyId);
            _isRegistered = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Unregister();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~HotkeyManager()
    {
        Dispose();
    }
}
