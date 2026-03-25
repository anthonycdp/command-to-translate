// src/Core/Config.cs
using Tomlyn;

namespace CommandToTranslate.Core;

public class AppConfig
{
    public OllamaConfig Ollama { get; set; } = new();
    public TranslationConfig Translation { get; set; } = new();
    public BehaviorConfig Behavior { get; set; } = new();
    public HotkeyConfig Hotkey { get; set; } = new();
    public UiConfig Ui { get; set; } = new();

    public static AppConfig Load()
    {
        TryMigrateLegacyConfig();

        if (!File.Exists(AppPaths.ConfigPath))
        {
            var defaultConfig = new AppConfig();
            defaultConfig.Save();
            return defaultConfig;
        }

        try
        {
            var content = File.ReadAllText(AppPaths.ConfigPath);
            return Toml.ToModel<AppConfig>(content).Normalize();
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
        Normalize();
        AppPaths.EnsureAppDirectories();
        var toml = Toml.FromModel(new PersistedAppConfig
        {
            Ollama = Ollama,
            Translation = Translation.ToPersistedModel(),
            Behavior = Behavior,
            Hotkey = Hotkey,
            Ui = Ui
        });
        File.WriteAllText(AppPaths.ConfigPath, toml);
    }

    internal static string GetConfigPath()
    {
        return AppPaths.ConfigPath;
    }

    internal AppConfig Normalize()
    {
        Ollama ??= new OllamaConfig();
        Translation ??= new TranslationConfig();
        Behavior ??= new BehaviorConfig();
        Hotkey ??= new HotkeyConfig();
        Ui ??= new UiConfig();

        Translation.Normalize();
        Hotkey.Normalize();
        return this;
    }

    private static void TryMigrateLegacyConfig()
    {
        if (File.Exists(AppPaths.ConfigPath) || !File.Exists(AppPaths.LegacyConfigPath))
            return;

        AppPaths.EnsureAppDirectories();
        File.Copy(AppPaths.LegacyConfigPath, AppPaths.ConfigPath, overwrite: false);
    }
}

public class OllamaConfig
{
    public string Endpoint { get; set; } = "http://127.0.0.1:11434";
    public string Model { get; set; } = "translategemma";
    public int TimeoutMs { get; set; } = 30000;
    public double Temperature { get; set; } = 0.0;
    public bool Stream { get; set; } = false;
    public string KeepAlive { get; set; } = "5m";
}

public sealed class TranslationConfig
{
    public string SourceLanguage { get; set; } = TranslationLanguageCatalog.DefaultSourceLanguageCode;
    public string TargetLanguage { get; set; } = TranslationLanguageCatalog.DefaultTargetLanguageCode;

    // Legacy fields kept for migration from the previous config shape.
    public string? ActivePair { get; set; }
    public List<TranslationPairConfig>? SupportedPairs { get; set; }

    internal void Normalize()
    {
        SourceLanguage = SourceLanguage?.Trim() ?? string.Empty;
        TargetLanguage = TargetLanguage?.Trim() ?? string.Empty;

        if (TranslationLanguageCatalog.TryCreatePair(SourceLanguage, TargetLanguage, out var pair))
        {
            SourceLanguage = pair.SourceLanguage.Code;
            TargetLanguage = pair.TargetLanguage.Code;
            return;
        }

        if (TryResolveLegacyPair(out pair))
        {
            SourceLanguage = pair.SourceLanguage.Code;
            TargetLanguage = pair.TargetLanguage.Code;
            return;
        }

        var defaultPair = TranslationLanguageCatalog.DefaultPair;
        SourceLanguage = defaultPair.SourceLanguage.Code;
        TargetLanguage = defaultPair.TargetLanguage.Code;
    }

    internal IReadOnlyList<TranslationLanguage> GetSupportedLanguages()
    {
        return TranslationLanguageCatalog.SupportedLanguages;
    }

    internal TranslationPair GetActivePair()
    {
        Normalize();
        if (!TranslationLanguageCatalog.TryCreatePair(SourceLanguage, TargetLanguage, out var pair))
            return TranslationLanguageCatalog.DefaultPair;

        return pair;
    }

    internal bool TrySetActiveLanguages(
        string? sourceLanguageCode,
        string? targetLanguageCode,
        out TranslationPair pair)
    {
        Normalize();

        if (!TranslationLanguageCatalog.TryCreatePair(sourceLanguageCode, targetLanguageCode, out pair))
        {
            pair = GetActivePair();
            return false;
        }

        SourceLanguage = pair.SourceLanguage.Code;
        TargetLanguage = pair.TargetLanguage.Code;
        return true;
    }

    private bool TryResolveLegacyPair(out TranslationPair pair)
    {
        if (TryResolveActivePair(out pair))
            return true;

        if (SupportedPairs is null)
        {
            pair = TranslationLanguageCatalog.DefaultPair;
            return false;
        }

        foreach (var legacyPair in SupportedPairs.Where(pairConfig => pairConfig is not null))
        {
            if (TranslationLanguageCatalog.TryCreatePair(legacyPair.Source, legacyPair.Target, out pair))
                return true;
        }

        pair = TranslationLanguageCatalog.DefaultPair;
        return false;
    }

    private bool TryResolveActivePair(out TranslationPair pair)
    {
        if (!string.IsNullOrWhiteSpace(ActivePair))
        {
            var tokens = ActivePair.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 2 &&
                TranslationLanguageCatalog.TryCreatePair(tokens[0], tokens[1], out pair))
            {
                return true;
            }
        }

        pair = TranslationLanguageCatalog.DefaultPair;
        return false;
    }

    internal PersistedTranslationConfig ToPersistedModel()
    {
        Normalize();
        return new PersistedTranslationConfig
        {
            SourceLanguage = SourceLanguage,
            TargetLanguage = TargetLanguage
        };
    }
}

public sealed class TranslationPairConfig
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
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

    internal void Normalize()
    {
        if (!HotkeyBindingParser.TryCreate(Modifiers, Key, out var binding))
            binding = HotkeyBindingParser.DefaultBinding;

        ApplyBinding(binding);
    }

    internal HotkeyBinding GetBinding()
    {
        Normalize();
        HotkeyBindingParser.TryCreate(Modifiers, Key, out var binding);
        return binding;
    }

    internal bool TrySetBinding(
        IEnumerable<string>? modifiers,
        string? key,
        out HotkeyBinding binding)
    {
        if (!HotkeyBindingParser.TryCreate(modifiers, key, out binding))
        {
            binding = GetBinding();
            return false;
        }

        ApplyBinding(binding);
        return true;
    }

    internal void ApplyBinding(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        Modifiers = binding.Modifiers.ToList();
        Key = binding.Key;
    }
}

public class UiConfig
{
    public bool ShowNotifications { get; set; } = true;
    public bool NotifyOnError { get; set; } = true;
}

internal sealed class PersistedAppConfig
{
    public OllamaConfig Ollama { get; set; } = new();
    public PersistedTranslationConfig Translation { get; set; } = new();
    public BehaviorConfig Behavior { get; set; } = new();
    public HotkeyConfig Hotkey { get; set; } = new();
    public UiConfig Ui { get; set; } = new();
}

internal sealed class PersistedTranslationConfig
{
    public string SourceLanguage { get; set; } = TranslationLanguageCatalog.DefaultSourceLanguageCode;
    public string TargetLanguage { get; set; } = TranslationLanguageCatalog.DefaultTargetLanguageCode;
}
