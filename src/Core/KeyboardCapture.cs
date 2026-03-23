namespace CommandToTranslate.Core;

public enum KbEventType
{
    Char,
    Space,
    Punctuation,
    Enter,
    Backspace
}

public readonly record struct KbEvent(
    char? Character,
    KbEventType Type,
    IntPtr WindowHandle,
    bool IsPasswordField);
