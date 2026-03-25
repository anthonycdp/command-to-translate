namespace CommandToTranslate.Core;

public sealed record FocusedContext(
    IntPtr WindowHandle,
    IntPtr FocusedWindowHandle,
    string ProcessName,
    string WindowClassName,
    string FocusedWindowClassName,
    bool IsPasswordField
);
