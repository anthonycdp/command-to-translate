using System.Diagnostics;
using System.Windows.Forms;
using RealTranslate.Core;
using RealTranslate.Hooks;
using RealTranslate.Services;
using RealTranslate.UI;

namespace RealTranslate;

/// <summary>
/// Application entry point. Wires all components together and manages the application lifecycle.
/// </summary>
static class Program
{
    private static readonly CancellationTokenSource AppCancellation = new();
    private static AppState? _state;
    private static TranslationService? _translationService;
    private static BufferManager? _bufferManager;
    private static Injector? _injector;
    private static KeyboardHook? _keyboardHook;
    private static HotkeyManager? _hotkeyManager;
    private static TrayIcon? _trayIcon;

    [STAThread]
    static void Main()
    {
        // Set up application
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            // Step 1: Load configuration
            var config = AppConfig.Load();

            // Step 2: Create AppState with config
            _state = new AppState { Config = config };

            // Step 3: Create all services
            _translationService = new TranslationService(_state);
            _bufferManager = new BufferManager(_state);
            _injector = new Injector(_state, _bufferManager);

            // Step 4: Create hooks
            _keyboardHook = new KeyboardHook(_state);

            // Step 5: Create UI - need a message-only window for hotkeys
            _trayIcon = new TrayIcon(_state);

            // Create a hidden form for receiving hotkey messages
            using var messageWindow = new MessageWindow();
            _hotkeyManager = new HotkeyManager(_state, messageWindow.Handle);

            // Step 6: Register hotkey
            if (!_hotkeyManager.Register())
            {
                _trayIcon.ShowNotification(
                    "real-translate",
                    "Failed to register hotkey. It may be in use by another application.",
                    ToolTipIcon.Warning);
            }

            // Step 7: Wire events
            _hotkeyManager.HotkeyPressed += (s, e) =>
            {
                _state!.IsPaused = !_state.IsPaused;
                _trayIcon!.UpdateIcon();
            };

            _trayIcon.ExitRequested += (s, e) =>
            {
                AppCancellation.Cancel();
                Application.Exit();
            };

            _trayIcon.ToggleRequested += (s, e) =>
            {
                _state!.IsPaused = !_state.IsPaused;
                _trayIcon!.UpdateIcon();

                var status = _state.IsPaused ? "Paused" : "Active";
                _trayIcon.ShowNotification(
                    "real-translate",
                    $"Translation {status}",
                    ToolTipIcon.Info);
            };

            // Hook up message window to hotkey manager
            messageWindow.HotkeyMessageReceived += (msg, wParam) =>
            {
                return _hotkeyManager.ProcessMessage(msg, wParam);
            };

            // Step 8: Start keyboard hook
            _keyboardHook.Start();

            // Step 9: Start processing loops (3 background tasks)
            var keyboardProcessorTask = StartKeyboardEventProcessor();
            var translationProcessorTask = StartTranslationProcessor();
            var injectionProcessorTask = StartInjectionProcessor();

            // Step 10: Health check loop (every 30s)
            var healthCheckTask = StartHealthCheckLoop();

            // Step 11: Initial health check
            _ = PerformInitialHealthCheck();

            // Step 12: Run message loop
            Application.Run(messageWindow);

            // Step 13: Cleanup on exit
            Cleanup();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Fatal error: {ex.Message}",
                "real-translate",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Processes keyboard events from the channel and sends them to BufferManager.
    /// Results in translation tasks being queued.
    /// </summary>
    private static async Task StartKeyboardEventProcessor()
    {
        var reader = AppChannels.KeyboardEvents.Reader;

        try
        {
            await foreach (var kbEvent in reader.ReadAllAsync(AppCancellation.Token))
            {
                if (AppCancellation.Token.IsCancellationRequested)
                    break;

                var translationTask = _bufferManager!.ProcessEvent(kbEvent);

                if (translationTask != null)
                {
                    await AppChannels.TranslationTasks.Writer.WriteAsync(
                        translationTask,
                        AppCancellation.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Keyboard processor error: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes translation tasks from the channel, calls TranslationService,
    /// and queues results for injection.
    /// </summary>
    private static async Task StartTranslationProcessor()
    {
        var reader = AppChannels.TranslationTasks.Reader;

        try
        {
            await foreach (var task in reader.ReadAllAsync(AppCancellation.Token))
            {
                if (AppCancellation.Token.IsCancellationRequested)
                    break;

                // Skip if paused or Ollama unavailable
                if (_state!.IsPaused || !_state.OllamaAvailable)
                    continue;

                // Translate the text
                var translated = await _translationService!.TranslateAsync(
                    task.Text,
                    task.CancellationToken);

                if (string.IsNullOrEmpty(translated))
                    continue;

                // Create injection task
                var injectionTask = new InjectionTask(
                    translated,
                    task.CharactersToDelete,
                    task.Mode == TranslationMode.PhraseWithContext);

                await AppChannels.InjectionTasks.Writer.WriteAsync(
                    injectionTask,
                    AppCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Translation processor error: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes injection tasks from the channel and injects translated text.
    /// </summary>
    private static async Task StartInjectionProcessor()
    {
        var reader = AppChannels.InjectionTasks.Reader;

        try
        {
            await foreach (var task in reader.ReadAllAsync(AppCancellation.Token))
            {
                if (AppCancellation.Token.IsCancellationRequested)
                    break;

                // Skip if paused
                if (_state!.IsPaused)
                    continue;

                // Inject the translated text
                if (task.IsRefinement)
                {
                    await _injector!.InjectRefinementAsync(task.TranslatedText, AppCancellation.Token);
                }
                else
                {
                    await _injector!.InjectAsync(task, AppCancellation.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Injection processor error: {ex.Message}");
        }
    }

    /// <summary>
    /// Periodic health check to verify Ollama availability (every 30 seconds).
    /// </summary>
    private static async Task StartHealthCheckLoop()
    {
        while (!AppCancellation.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), AppCancellation.Token);

                var (isHealthy, errorMessage) = await _translationService!.CheckHealthAsync();

                var wasAvailable = _state!.OllamaAvailable;
                _state.OllamaAvailable = isHealthy;

                // Update tray icon if availability changed
                if (wasAvailable != isHealthy)
                {
                    _trayIcon!.UpdateIcon();

                    if (!isHealthy && _state.Config.Ui.NotifyOnError)
                    {
                        _trayIcon.ShowNotification(
                            "real-translate - Error",
                            errorMessage ?? "Ollama unavailable",
                            ToolTipIcon.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Health check error: {ex.Message}");
                _state!.OllamaAvailable = false;
                _trayIcon!.UpdateIcon();
            }
        }
    }

    /// <summary>
    /// Performs initial health check on startup.
    /// </summary>
    private static async Task PerformInitialHealthCheck()
    {
        try
        {
            var (isHealthy, errorMessage) = await _translationService!.CheckHealthAsync();

            _state!.OllamaAvailable = isHealthy;
            _trayIcon!.UpdateIcon();

            if (!isHealthy && _state.Config.Ui.NotifyOnError)
            {
                _trayIcon.ShowNotification(
                    "real-translate - Startup Error",
                    errorMessage ?? "Ollama unavailable. Please start Ollama.",
                    ToolTipIcon.Error);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Initial health check error: {ex.Message}");
            _state!.OllamaAvailable = false;
            _trayIcon!.UpdateIcon();
        }
    }

    /// <summary>
    /// Cleanup all resources on application exit.
    /// </summary>
    private static void Cleanup()
    {
        AppCancellation.Cancel();

        _keyboardHook?.Dispose();
        _hotkeyManager?.Dispose();
        _trayIcon?.Dispose();
        _translationService?.Dispose();
        _bufferManager?.Dispose();
        AppCancellation.Dispose();
    }
}

/// <summary>
/// Hidden window for receiving Windows messages, particularly hotkey notifications.
/// </summary>
internal class MessageWindow : Form
{
    private const int WM_HOTKEY = 0x0312;
    private const int WM_DESTROY = 0x0002;

    /// <summary>
    /// Event raised when a Windows message is received.
    /// Returns true if the message was handled.
    /// </summary>
    public event Func<int, IntPtr, bool>? HotkeyMessageReceived;

    public MessageWindow()
    {
        // Create invisible window
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Opacity = 0;
        Size = new Size(0, 0);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            var handled = HotkeyMessageReceived?.Invoke(m.Msg, m.WParam) ?? false;
            if (handled)
                return;
        }

        base.WndProc(ref m);
    }

    protected override void SetVisibleCore(bool value)
    {
        // Ensure window is never visible
        base.SetVisibleCore(false);
    }
}
