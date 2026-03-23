using System.Windows.Forms;
using CommandToTranslate.Native;

namespace CommandToTranslate.Core;

/// <summary>
/// Immutable runtime representation of the configured hotkey.
/// </summary>
public sealed record HotkeyBinding(IReadOnlyList<string> Modifiers, string Key)
{
    public string Label => string.Join(" + ", Modifiers.Append(Key));
}

/// <summary>
/// Parsing and formatting helpers for supported hotkeys.
/// </summary>
public static class HotkeyBindingParser
{
    public static HotkeyBinding DefaultBinding { get; } = new(["Ctrl", "Shift"], "T");

    public static bool TryCreate(
        IEnumerable<string>? modifiers,
        string? key,
        out HotkeyBinding binding)
    {
        var normalizedModifiers = NormalizeModifiers(modifiers).ToArray();
        var normalizedKey = NormalizeKey(key);

        if (normalizedModifiers.Length == 0 || ParseKeyCode(normalizedKey) == 0)
        {
            binding = DefaultBinding;
            return false;
        }

        binding = new HotkeyBinding(normalizedModifiers, normalizedKey);
        return true;
    }

    public static bool TryCreateFromKeyEvent(KeyEventArgs e, out HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(e);

        var modifiers = new List<string>();

        if (e.Control)
            modifiers.Add("Ctrl");
        if (e.Shift)
            modifiers.Add("Shift");
        if (e.Alt)
            modifiers.Add("Alt");
        if (IsWindowsKeyPressed())
            modifiers.Add("Win");

        var key = NormalizeKey(e.KeyCode.ToString());
        if (IsModifierOnlyKey(e.KeyCode))
            key = string.Empty;

        return TryCreate(modifiers, key, out binding);
    }

    public static uint ParseModifiersToFlags(IEnumerable<string>? modifiers)
    {
        uint result = 0;

        foreach (var modifier in NormalizeModifiers(modifiers))
        {
            result |= modifier switch
            {
                "Ctrl" => Win32.MOD_CONTROL,
                "Shift" => Win32.MOD_SHIFT,
                "Alt" => Win32.MOD_ALT,
                "Win" => Win32.MOD_WIN,
                _ => 0
            };
        }

        return result;
    }

    public static uint ParseKeyCode(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return 0;

        var normalizedKey = NormalizeKey(key);

        if (normalizedKey.Length == 1)
        {
            var character = normalizedKey[0];
            if (character >= 'A' && character <= 'Z')
                return character;
            if (character >= '0' && character <= '9')
                return character;
        }

        return normalizedKey switch
        {
            "Space" => 0x20,
            "Enter" => 0x0D,
            "Tab" => 0x09,
            "Escape" => 0x1B,
            "Backspace" => 0x08,
            "Insert" => 0x2D,
            "Delete" => 0x2E,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            "Up" => 0x26,
            "Down" => 0x28,
            "Left" => 0x25,
            "Right" => 0x27,
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
            "Numpad0" => 0x60,
            "Numpad1" => 0x61,
            "Numpad2" => 0x62,
            "Numpad3" => 0x63,
            "Numpad4" => 0x64,
            "Numpad5" => 0x65,
            "Numpad6" => 0x66,
            "Numpad7" => 0x67,
            "Numpad8" => 0x68,
            "Numpad9" => 0x69,
            "Multiply" => 0x6A,
            "Add" => 0x6B,
            "Separator" => 0x6C,
            "Subtract" => 0x6D,
            "Decimal" => 0x6E,
            "Divide" => 0x6F,
            _ => 0
        };
    }

    public static string NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var normalized = key.Trim().ToUpperInvariant();

        if (normalized.Length == 1)
            return normalized;

        return normalized switch
        {
            "D0" => "0",
            "D1" => "1",
            "D2" => "2",
            "D3" => "3",
            "D4" => "4",
            "D5" => "5",
            "D6" => "6",
            "D7" => "7",
            "D8" => "8",
            "D9" => "9",
            "RETURN" or "ENTER" => "Enter",
            "ESC" or "ESCAPE" => "Escape",
            "BACK" or "BACKSPACE" => "Backspace",
            "SPACE" => "Space",
            "TAB" => "Tab",
            "INSERT" => "Insert",
            "DELETE" => "Delete",
            "HOME" => "Home",
            "END" => "End",
            "PRIOR" => "PageUp",
            "NEXT" => "PageDown",
            "PAGEUP" => "PageUp",
            "PAGEDOWN" => "PageDown",
            "UP" => "Up",
            "DOWN" => "Down",
            "LEFT" => "Left",
            "RIGHT" => "Right",
            "MULTIPLY" => "Multiply",
            "ADD" => "Add",
            "SEPARATOR" => "Separator",
            "SUBTRACT" => "Subtract",
            "DECIMAL" => "Decimal",
            "DIVIDE" => "Divide",
            _ when normalized.StartsWith("NUMPAD", StringComparison.Ordinal) => normalized[..1] + normalized[1..].ToLowerInvariant(),
            _ when normalized.StartsWith("F", StringComparison.Ordinal) => normalized,
            _ => normalized[..1] + normalized[1..].ToLowerInvariant()
        };
    }

    private static IEnumerable<string> NormalizeModifiers(IEnumerable<string>? modifiers)
    {
        if (modifiers is null)
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var modifier in modifiers)
        {
            var normalized = NormalizeModifier(modifier);
            if (!string.IsNullOrEmpty(normalized))
                seen.Add(normalized);
        }

        foreach (var modifier in new[] { "Ctrl", "Shift", "Alt", "Win" })
        {
            if (seen.Contains(modifier))
                yield return modifier;
        }
    }

    private static string NormalizeModifier(string? modifier)
    {
        if (string.IsNullOrWhiteSpace(modifier))
            return string.Empty;

        return modifier.Trim().ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => "Ctrl",
            "SHIFT" => "Shift",
            "ALT" => "Alt",
            "WIN" or "WINDOWS" => "Win",
            _ => string.Empty
        };
    }

    private static bool IsModifierOnlyKey(Keys keyCode)
    {
        return keyCode is
            Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
            Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
            Keys.Menu or Keys.LMenu or Keys.RMenu or
            Keys.LWin or Keys.RWin;
    }

    private static bool IsWindowsKeyPressed()
    {
        return (Win32.GetKeyState(Win32.VK_LWIN) & 0x8000) != 0 ||
               (Win32.GetKeyState(Win32.VK_RWIN) & 0x8000) != 0;
    }
}
