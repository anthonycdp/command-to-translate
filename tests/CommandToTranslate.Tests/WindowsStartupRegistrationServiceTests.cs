using CommandToTranslate.Services;
using Xunit;

namespace CommandToTranslate.Tests;

public sealed class WindowsStartupRegistrationServiceTests
{
    [Fact]
    public void GetStatus_ReturnsEnabled_WhenStoredCommandMatchesExecutable()
    {
        var store = new FakeRunKeyStore
        {
            StoredValue = WindowsStartupRegistrationService.FormatCommand(@"C:\Apps\command-to-translate.exe")
        };
        var service = new WindowsStartupRegistrationService(store, @"C:\Apps\command-to-translate.exe");

        var status = service.GetStatus();

        Assert.True(status.Success);
        Assert.True(status.IsEnabled);
    }

    [Fact]
    public void SetEnabled_WritesQuotedExecutablePath()
    {
        var store = new FakeRunKeyStore();
        var service = new WindowsStartupRegistrationService(store, @"C:\Apps\command-to-translate.exe");

        var status = service.SetEnabled(true);

        Assert.True(status.Success);
        Assert.Equal("\"C:\\Apps\\command-to-translate.exe\"", store.StoredValue);
    }

    [Fact]
    public void SetEnabled_False_RemovesStoredValue()
    {
        var store = new FakeRunKeyStore
        {
            StoredValue = WindowsStartupRegistrationService.FormatCommand(@"C:\Apps\command-to-translate.exe")
        };
        var service = new WindowsStartupRegistrationService(store, @"C:\Apps\command-to-translate.exe");

        var status = service.SetEnabled(false);

        Assert.True(status.Success);
        Assert.Null(store.StoredValue);
        Assert.False(status.IsEnabled);
    }

    [Fact]
    public void SetEnabled_ReturnsFailure_WhenStoreThrows()
    {
        var store = new FakeRunKeyStore
        {
            ThrowOnSet = true
        };
        var service = new WindowsStartupRegistrationService(store, @"C:\Apps\command-to-translate.exe");

        var status = service.SetEnabled(true);

        Assert.False(status.Success);
        Assert.False(status.IsEnabled);
        Assert.Contains("Failed to update Windows startup setting", status.Message);
    }

    private sealed class FakeRunKeyStore : IRunKeyStore
    {
        public string? StoredValue { get; set; }
        public bool ThrowOnSet { get; set; }
        public bool ThrowOnGet { get; set; }

        public object? GetValue(string name)
        {
            if (ThrowOnGet)
                throw new InvalidOperationException("read failure");

            return StoredValue;
        }

        public void SetValue(string name, string value)
        {
            if (ThrowOnSet)
                throw new InvalidOperationException("write failure");

            StoredValue = value;
        }

        public void DeleteValue(string name)
        {
            StoredValue = null;
        }
    }
}
