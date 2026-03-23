// src/Core/Config.cs
using Tomlyn;

namespace CommandToTranslate.Core;

public class AppConfig
{
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

public class OllamaConfig
{
    public string Endpoint { get; set; } = "http://127.0.0.1:11434";
    public string Model { get; set; } = "translategemma";
    public int TimeoutMs { get; set; } = 10000;
    public double Temperature { get; set; } = 0.0;
    public bool Stream { get; set; } = false;
    public string KeepAlive { get; set; } = "5m";
}

public class BehaviorConfig
{
    public int ShortcutStepDelayMs { get; set; } = 35;
    public int ClipboardTimeoutMs { get; set; } = 800;
    public int HostSettleDelayMs { get; set; } = 60;
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
