// src/Services/Logger.cs
using System.Text;

namespace CommandToTranslate.Services;

/// <summary>
/// Simple file logger for debugging.
/// Log file is created at: %APPDATA%\command-to-translate\logs\command-to-translate.log
/// </summary>
public static class Logger
{
    private static readonly string LogPath;
    private static readonly object _lock = new();
    private static bool _enabled = true;

    static Logger()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logDir = Path.Combine(appDataPath, "command-to-translate", "logs");
        Directory.CreateDirectory(logDir);
        LogPath = Path.Combine(logDir, $"command-to-translate-{DateTime.Now:yyyyMMdd}.log");
    }

    public static string LogFilePath => LogPath;

    public static void Enable(bool enabled) => _enabled = enabled;

    public static void Info(string message)
    {
        Log("INFO", message);
    }

    public static void Warning(string message)
    {
        Log("WARN", message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        var sb = new StringBuilder();
        sb.Append(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.Append($"  Exception: {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine();
            sb.Append($"  StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.Append($"  InnerException: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
        }
        Log("ERROR", sb.ToString());
    }

    public static void Debug(string message)
    {
#if DEBUG
        Log("DEBUG", message);
#endif
    }

    private static void Log(string level, string message)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
            catch
            {
                // Ignore logging errors
            }
        }
    }

    public static void LogStartup()
    {
        Info("=== command-to-translate starting ===");
        Info($"OS: {Environment.OSVersion}");
        Info($"Runtime: {Environment.Version}");
        Info($"Log file: {LogPath}");
    }

    public static void LogShutdown()
    {
        Info("=== command-to-translate shutting down ===");
    }
}
