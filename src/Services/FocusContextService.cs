using System.Diagnostics;
using CommandToTranslate.Core;
using CommandToTranslate.Native;

namespace CommandToTranslate.Services;

public interface IFocusContextService
{
    FocusedContext GetFocusedContext();
}

public sealed class FocusContextService : IFocusContextService
{
    public FocusedContext GetFocusedContext()
    {
        var windowHandle = Win32.GetForegroundWindow();
        var focusedWindowHandle = WindowInspector.GetFocusedWindowHandle(windowHandle);
        var processName = GetProcessName(windowHandle);
        var windowClassName = WindowInspector.GetClassName(windowHandle);
        var focusedWindowClassName = WindowInspector.GetClassName(focusedWindowHandle);
        var passwordHandle = focusedWindowHandle != IntPtr.Zero ? focusedWindowHandle : windowHandle;
        var isPasswordField = WindowInspector.IsPasswordField(passwordHandle);

        return new FocusedContext(
            windowHandle,
            focusedWindowHandle,
            processName,
            windowClassName,
            focusedWindowClassName,
            isPasswordField);
    }

    private static string GetProcessName(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return string.Empty;

        try
        {
            _ = Win32.GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId == 0)
                return string.Empty;

            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
