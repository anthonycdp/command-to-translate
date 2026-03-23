namespace CommandToTranslate.Core;

public sealed record FocusedContext(
    IntPtr WindowHandle,
    string ProcessName,
    string WindowClassName,
    bool IsPasswordField
);
