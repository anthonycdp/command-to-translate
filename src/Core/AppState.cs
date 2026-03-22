// src/Core/AppState.cs
namespace RealTranslate.Core;

/// <summary>
/// Global application state shared across components.
/// Uses volatile for simple flags, lock for complex state.
/// </summary>
public class AppState
{
    private readonly object _lock = new();

    // Volatile flags for lock-free reads
    public volatile bool IsPaused;
    public volatile bool IsInjecting;
    public volatile bool OllamaAvailable = true;

    // State requiring synchronization
    private bool _errorNotificationShown;
    private DateTime? _lastErrorTime;

    public AppConfig Config { get; set; } = null!;

    public bool ShouldTranslate => !IsPaused && !IsInjecting && OllamaAvailable;

    public bool TryMarkErrorNotification()
    {
        lock (_lock)
        {
            if (_errorNotificationShown)
                return false;

            _errorNotificationShown = true;
            _lastErrorTime = DateTime.UtcNow;
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
            _lastErrorTime = null;
            OllamaAvailable = true;
        }
    }
}
