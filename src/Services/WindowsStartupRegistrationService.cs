using Microsoft.Win32;

namespace CommandToTranslate.Services;

public readonly record struct StartupRegistrationStatus(bool Success, bool IsEnabled, string Message);

public interface IStartupRegistrationService
{
    StartupRegistrationStatus GetStatus();
    StartupRegistrationStatus SetEnabled(bool enabled);
}

internal interface IRunKeyStore
{
    object? GetValue(string name);
    void SetValue(string name, string value);
    void DeleteValue(string name);
}

internal sealed class RegistryRunKeyStore : IRunKeyStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public object? GetValue(string name)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return runKey?.GetValue(name);
    }

    public void SetValue(string name, string value)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        runKey.SetValue(name, value);
    }

    public void DeleteValue(string name)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        runKey?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    internal const string ValueName = "command-to-translate";

    private readonly IRunKeyStore _runKeyStore;
    private readonly string _expectedCommand;

    public WindowsStartupRegistrationService()
        : this(new RegistryRunKeyStore(), Environment.ProcessPath)
    {
    }

    internal WindowsStartupRegistrationService(IRunKeyStore runKeyStore, string? executablePath)
    {
        _runKeyStore = runKeyStore ?? throw new ArgumentNullException(nameof(runKeyStore));

        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path must be provided.", nameof(executablePath));

        _expectedCommand = FormatCommand(executablePath);
    }

    public StartupRegistrationStatus GetStatus()
    {
        try
        {
            var configuredValue = _runKeyStore.GetValue(ValueName)?.ToString();
            var isEnabled = CommandsMatch(configuredValue, _expectedCommand);
            return new StartupRegistrationStatus(
                true,
                isEnabled,
                isEnabled
                    ? "Start with Windows enabled."
                    : "Start with Windows disabled.");
        }
        catch (Exception ex)
        {
            return new StartupRegistrationStatus(
                false,
                false,
                $"Failed to read Windows startup setting: {ex.Message}");
        }
    }

    public StartupRegistrationStatus SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                _runKeyStore.SetValue(ValueName, _expectedCommand);
            }
            else
            {
                _runKeyStore.DeleteValue(ValueName);
            }

            return new StartupRegistrationStatus(
                true,
                enabled,
                enabled
                    ? "Start with Windows enabled."
                    : "Start with Windows disabled.");
        }
        catch (Exception ex)
        {
            var currentStatus = GetStatus();
            return new StartupRegistrationStatus(
                false,
                currentStatus.Success && currentStatus.IsEnabled,
                $"Failed to update Windows startup setting: {ex.Message}");
        }
    }

    internal static string FormatCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{Path.GetFullPath(executablePath)}\"";
    }

    private static bool CommandsMatch(string? configuredValue, string expectedCommand)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
            return false;

        return string.Equals(
            NormalizeCommand(configuredValue),
            NormalizeCommand(expectedCommand),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCommand(string command)
    {
        return command.Trim().Trim('"');
    }
}
