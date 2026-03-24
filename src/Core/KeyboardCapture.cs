namespace CommandToTranslate.Core;

public enum KbEventType
{
    Char,
    Space,
    Punctuation,
    Enter,
    Backspace,
    CursorLeft,
    CursorRight,
    Home,
    End,
    Delete,
    Paste,
    HistoryNavigation
}

public readonly record struct KbEvent(
    char? Character,
    KbEventType Type,
    IntPtr WindowHandle,
    bool IsPasswordField,
    string? Text = null);
