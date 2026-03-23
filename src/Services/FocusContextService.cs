using System.Diagnostics;
using System.Runtime.InteropServices;
using CommandToTranslate.Core;
using CommandToTranslate.Native;

namespace CommandToTranslate.Services;

public interface IFocusContextService
{
    FocusedContext GetFocusedContext();
}

public sealed class FocusContextService : IFocusContextService
{
    private static readonly Guid AccessibleId = new(
        0x618736E0, 0x3C3D, 0x11CF, 0x81, 0x0C, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    private const uint ObjectIdClient = 0xFFFFFFFC;
    private const int ProtectedState = 0x10;

    public FocusedContext GetFocusedContext()
    {
        var windowHandle = Win32.GetForegroundWindow();
        var processName = GetProcessName(windowHandle);
        var windowClassName = GetWindowClassName(windowHandle);
        var isPasswordField = IsPasswordField(windowHandle);

        return new FocusedContext(windowHandle, processName, windowClassName, isPasswordField);
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

    private static string GetWindowClassName(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return string.Empty;

        var className = new System.Text.StringBuilder(256);
        var length = NativeMethods.GetClassName(windowHandle, className, className.Capacity);
        return length > 0 ? className.ToString(0, length) : string.Empty;
    }

    private static bool IsPasswordField(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return false;

        try
        {
            IntPtr accessiblePointer = IntPtr.Zero;
            var accessibleId = AccessibleId;
            var result = NativeMethods.AccessibleObjectFromWindow(
                windowHandle,
                ObjectIdClient,
                ref accessibleId,
                out accessiblePointer);

            if (result != 0 || accessiblePointer == IntPtr.Zero)
                return HasPasswordStyle(windowHandle);

            try
            {
                var accessibleObject = Marshal.GetObjectForIUnknown(accessiblePointer);
                if (accessibleObject == null)
                    return HasPasswordStyle(windowHandle);

                dynamic accessible = accessibleObject;
                object state = accessible.accState;
                return state is int stateValue && (stateValue & ProtectedState) != 0;
            }
            catch
            {
                return HasPasswordStyle(windowHandle);
            }
            finally
            {
                Marshal.Release(accessiblePointer);
            }
        }
        catch
        {
            return HasPasswordStyle(windowHandle);
        }
    }

    private static bool HasPasswordStyle(IntPtr windowHandle)
    {
        var style = NativeMethods.GetWindowLongPtr(windowHandle, -16);
        const long passwordStyle = 0x0020;
        return ((long)style & passwordStyle) != 0;
    }
}

internal static class NativeMethods
{
    [DllImport("oleacc.dll", SetLastError = true)]
    public static extern int AccessibleObjectFromWindow(
        IntPtr hwnd,
        uint objectId,
        ref Guid interfaceId,
        out IntPtr accessible);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
}
