// src/Core/Config.cs
using Tomlyn;

namespace RealTranslate.Core;

public class AppConfig
{
    public TranslationConfig Translation { get; set; } = new();
    public OllamaConfig Ollama { get; set; } = new();
    public BehaviorConfig Behavior { get; set; } = new();
    public HotkeyConfig Hotkey { get; set; } = new();
    public UiConfig Ui { get; set; } = new();

    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.toml");

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new AppConfig();
            defaultConfig.Save();
            return defaultConfig;
        }

        try
        {
            var content = File.ReadAllText(ConfigPath);
            return Toml.ToModel<AppConfig>(content);
        }
        catch (TomlException)
        {
            // Malformed TOML - return default config
            var defaultConfig = new AppConfig();
            defaultConfig.Save();
            return defaultConfig;
        }
        catch (IOException)
        {
            // File read error - return default config
            return new AppConfig();
        }
    }

    public void Save()
    {
        var toml = Toml.FromModel(this);
        File.WriteAllText(ConfigPath, toml);
    }
}

public class TranslationConfig
{
    public string SourceLang { get; set; } = "pt-BR";
    public string TargetLang { get; set; } = "en-US";
}

public class OllamaConfig
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "translategemma";
    public int TimeoutMs { get; set; } = 2000;
    public double Temperature { get; set; } = 0.1;
    public bool Stream { get; set; } = false;
}

public class BehaviorConfig
{
    public int DebounceMs { get; set; } = 500;
    public int InjectDelayMs { get; set; } = 10;
    public int LoopProtectionMs { get; set; } = 50;
}

public class HotkeyConfig
{
    public List<string> Modifiers { get; set; } = new() { "Ctrl", "Shift" };
    public string Key { get; set; } = "T";
}

public class UiConfig
{
    public bool ShowNotifications { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;
}
