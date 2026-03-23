namespace CommandToTranslate.Core;

internal static class AppPaths
{
    private const string AppDirectoryName = "command-to-translate";

    private static string? _baseDirectoryOverride;
    private static string? _appDataRootOverride;

    public static string BaseDirectory =>
        EnsureTrailingSeparator(_baseDirectoryOverride ?? AppDomain.CurrentDomain.BaseDirectory);

    public static string AppDataRoot =>
        _appDataRootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDirectoryName);

    public static string ConfigPath => Path.Combine(AppDataRoot, "config.toml");

    public static string LegacyConfigPath => Path.Combine(BaseDirectory, "config.toml");

    public static string LogDirectory => Path.Combine(AppDataRoot, "logs");

    public static void EnsureAppDirectories()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(LogDirectory);
    }

    internal static void OverrideForTests(string? baseDirectory = null, string? appDataRoot = null)
    {
        _baseDirectoryOverride = baseDirectory is null
            ? null
            : EnsureTrailingSeparator(Path.GetFullPath(baseDirectory));
        _appDataRootOverride = appDataRoot is null
            ? null
            : Path.GetFullPath(appDataRoot);
    }

    internal static void ResetForTests()
    {
        _baseDirectoryOverride = null;
        _appDataRootOverride = null;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
