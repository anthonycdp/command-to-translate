// src/Core/AppState.cs
namespace CommandToTranslate.Core;

/// <summary>
/// Mutable runtime state shared by the long-lived application services.
/// </summary>
public sealed class AppState
{
    private readonly object _lock = new();
    private AppConfig _config = new();
    private TranslationPair _activeTranslationPair = TranslationLanguageCatalog.DefaultPair;
    private HotkeyBinding _activeHotkeyBinding = HotkeyBindingParser.DefaultBinding;

    public volatile bool IsPaused;
    public volatile bool OllamaAvailable = true;
    private bool _errorNotificationShown;

    public AppConfig Config
    {
        get
        {
            lock (_lock)
            {
                return _config;
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            lock (_lock)
            {
                _config = value.Normalize();
                _activeTranslationPair = _config.Translation.GetActivePair();
                _activeHotkeyBinding = _config.Hotkey.GetBinding();
            }
        }
    }

    public bool IsEnabled => !IsPaused;

    public TranslationPair ActiveTranslationPair
    {
        get
        {
            lock (_lock)
            {
                return _activeTranslationPair;
            }
        }
    }

    public HotkeyBinding ActiveHotkeyBinding
    {
        get
        {
            lock (_lock)
            {
                return _activeHotkeyBinding;
            }
        }
    }

    public IReadOnlyList<TranslationLanguage> GetSupportedTranslationLanguages()
    {
        lock (_lock)
        {
            return _config.Translation.GetSupportedLanguages();
        }
    }

    public bool TrySetActiveTranslationPair(
        string sourceLanguageCode,
        string targetLanguageCode,
        out TranslationPair pair)
    {
        lock (_lock)
        {
            var changed = _config.Translation.TrySetActiveLanguages(
                sourceLanguageCode,
                targetLanguageCode,
                out pair);
            _activeTranslationPair = pair;
            return changed;
        }
    }

    public bool TrySetActiveHotkeyBinding(
        IEnumerable<string>? modifiers,
        string? key,
        out HotkeyBinding binding)
    {
        lock (_lock)
        {
            var changed = _config.Hotkey.TrySetBinding(modifiers, key, out binding);
            _activeHotkeyBinding = binding;
            return changed;
        }
    }

    public void SetActiveHotkeyBinding(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        lock (_lock)
        {
            _config.Hotkey.ApplyBinding(binding);
            _activeHotkeyBinding = binding;
        }
    }

    public bool TryMarkErrorNotification()
    {
        lock (_lock)
        {
            if (_errorNotificationShown)
                return false;

            _errorNotificationShown = true;
            return true;
        }
    }

    public void ClearErrorNotification()
    {
        lock (_lock)
        {
            _errorNotificationShown = false;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _errorNotificationShown = false;
            OllamaAvailable = true;
        }
    }
}
