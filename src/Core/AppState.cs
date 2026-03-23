// src/Core/AppState.cs
namespace CommandToTranslate.Core;

/// <summary>
/// Mutable runtime state shared by the long-lived application services.
/// </summary>
public sealed class AppState
{
    private readonly object _lock = new();
    public volatile bool IsPaused;
    public volatile bool OllamaAvailable = true;
    private bool _errorNotificationShown;

    public AppConfig Config { get; set; } = null!;

    public bool IsEnabled => !IsPaused;

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
