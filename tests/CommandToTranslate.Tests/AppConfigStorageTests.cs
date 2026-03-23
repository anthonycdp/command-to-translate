using CommandToTranslate.Core;
using Xunit;

namespace CommandToTranslate.Tests;

public sealed class AppConfigStorageTests : IDisposable
{
    public AppConfigStorageTests()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "command-to-translate-tests", Guid.NewGuid().ToString("N"));
        BaseDirectory = Path.Combine(testRoot, "base");
        AppDataRoot = Path.Combine(testRoot, "appdata");

        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(AppDataRoot);

        AppPaths.OverrideForTests(BaseDirectory, AppDataRoot);
    }

    private string BaseDirectory { get; }
    private string AppDataRoot { get; }

    [Fact]
    public void Load_CreatesDefaultConfigInAppData_WhenMissing()
    {
        var config = AppConfig.Load();

        Assert.Equal(AppPaths.ConfigPath, AppConfig.GetConfigPath());
        Assert.True(File.Exists(AppPaths.ConfigPath));
        Assert.Equal("pt-BR", config.Translation.SourceLanguage);
        Assert.False(File.Exists(AppPaths.LegacyConfigPath));
    }

    [Fact]
    public void Load_CopiesLegacyConfigToAppData_WhenNoAppDataConfigExists()
    {
        var legacyConfig = new AppConfig();
        legacyConfig.Translation.SourceLanguage = "en-US";
        legacyConfig.Translation.TargetLanguage = "pt-BR";
        legacyConfig.Save();
        File.Copy(AppPaths.ConfigPath, AppPaths.LegacyConfigPath, overwrite: true);
        File.Delete(AppPaths.ConfigPath);

        var config = AppConfig.Load();

        Assert.True(File.Exists(AppPaths.ConfigPath));
        Assert.True(File.Exists(AppPaths.LegacyConfigPath));
        Assert.Equal("en-US", config.Translation.SourceLanguage);
        Assert.Equal("pt-BR", config.Translation.TargetLanguage);
    }

    [Fact]
    public void Load_PrefersExistingAppDataConfig_OverLegacyConfig()
    {
        var legacyConfig = new AppConfig();
        legacyConfig.Translation.SourceLanguage = "en-US";
        legacyConfig.Translation.TargetLanguage = "pt-BR";
        legacyConfig.Save();
        File.Copy(AppPaths.ConfigPath, AppPaths.LegacyConfigPath, overwrite: true);

        var appDataConfig = new AppConfig();
        appDataConfig.Translation.SourceLanguage = "fr-FR";
        appDataConfig.Translation.TargetLanguage = "de-DE";
        appDataConfig.Save();

        var config = AppConfig.Load();

        Assert.Equal("fr-FR", config.Translation.SourceLanguage);
        Assert.Equal("de-DE", config.Translation.TargetLanguage);
    }

    public void Dispose()
    {
        AppPaths.ResetForTests();

        try
        {
            var root = Directory.GetParent(BaseDirectory)?.FullName;
            if (root is not null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup failures on Windows file locks.
        }
    }
}
