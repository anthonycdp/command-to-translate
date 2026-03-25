using System.Runtime.InteropServices;

namespace CommandToTranslate.Native;

internal static class WindowInspector
{
    private const int ProtectedState = 0x10;
    private const int WindowStyleIndex = -16;
    private const long PasswordStyle = 0x0020;

    public static string GetClassName(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return string.Empty;

        var className = new System.Text.StringBuilder(256);
        var length = Win32.GetClassName(windowHandle, className, className.Capacity);
        return length > 0 ? className.ToString(0, length) : string.Empty;
    }

    public static IntPtr GetFocusedWindowHandle(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return IntPtr.Zero;

        var threadId = Win32.GetWindowThreadProcessId(windowHandle, out _);
        if (threadId == 0)
            return IntPtr.Zero;

        var guiThreadInfo = new Win32.GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<Win32.GUITHREADINFO>()
        };

        return Win32.GetGUIThreadInfo(threadId, ref guiThreadInfo)
            ? guiThreadInfo.hwndFocus
            : IntPtr.Zero;
    }

    public static bool IsPasswordField(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return false;

        if (TryGetAccessibilityState(windowHandle, out var accessibilityState))
            return (accessibilityState & ProtectedState) != 0;

        return HasPasswordStyle(windowHandle) || UsesPasswordClassName(windowHandle);
    }

    private static bool TryGetAccessibilityState(IntPtr windowHandle, out int accessibilityState)
    {
        accessibilityState = 0;
        IntPtr accessiblePointer = IntPtr.Zero;

        try
        {
            var accessibleId = Win32.IID_IAccessible;
            var result = Win32.AccessibleObjectFromWindow(
                windowHandle,
                Win32.OBJID_CLIENT,
                ref accessibleId,
                out accessiblePointer);

            if (result != 0 || accessiblePointer == IntPtr.Zero)
                return false;

            var accessibleObject = Marshal.GetObjectForIUnknown(accessiblePointer);
            if (accessibleObject is null)
                return false;

            dynamic accessible = accessibleObject;
            object state = accessible.accState;
            if (state is not int stateValue)
                return false;

            accessibilityState = stateValue;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (accessiblePointer != IntPtr.Zero)
                Marshal.Release(accessiblePointer);
        }
    }

    private static bool HasPasswordStyle(IntPtr windowHandle)
    {
        return ((long)Win32.GetWindowLongPtr(windowHandle, WindowStyleIndex) & PasswordStyle) != 0;
    }

    private static bool UsesPasswordClassName(IntPtr windowHandle)
    {
        var className = GetClassName(windowHandle);
        if (string.IsNullOrWhiteSpace(className))
            return false;

        return className.Contains("password", StringComparison.OrdinalIgnoreCase);
    }
}
