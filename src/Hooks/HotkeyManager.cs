// src/Hooks/HotkeyManager.cs
using System.Runtime.InteropServices;
using CommandToTranslate.Core;
using CommandToTranslate.Native;
using CommandToTranslate.Services;

namespace CommandToTranslate.Hooks;

/// <summary>
/// Manages global hotkey registration for on-demand translation.
/// Uses the RegisterHotKey Win32 API for system-wide hotkey detection.
/// </summary>
public class HotkeyManager : IDisposable, IHotkeyRegistrar
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
    /// <param name="appState">The application state containing hotkey configuration.</param>
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

        return Register(_appState.ActiveHotkeyBinding);
    }

    public bool Register(HotkeyBinding binding)
    {
        if (_isRegistered)
            return true;

        ArgumentNullException.ThrowIfNull(binding);

        uint modifiers = ParseModifiers(binding.Modifiers.ToList());
        uint key = ParseKey(binding.Key);
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
        return HotkeyBindingParser.ParseModifiersToFlags(modifiers);
    }

    /// <summary>
    /// Parses a key name into a virtual key code.
    /// </summary>
    /// <param name="key">Key name (e.g., "T", "F1", "Space", "Enter").</param>
    /// <returns>Virtual key code, or 0 if invalid.</returns>
    public static uint ParseKey(string? key)
    {
        return HotkeyBindingParser.ParseKeyCode(key);
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
