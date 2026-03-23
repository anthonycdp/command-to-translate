using System.Runtime.InteropServices;
using CommandToTranslate.Native;
using Xunit;

namespace CommandToTranslate.Tests;

public class Win32InteropTests
{
    [Fact]
    public void InputStructHasExpectedSizeForCurrentArchitecture()
    {
        var expectedSize = IntPtr.Size == 8 ? 40 : 28;
        Assert.Equal(expectedSize, Marshal.SizeOf<Win32.INPUT>());
    }
}
