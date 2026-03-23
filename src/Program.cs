using System.Windows.Forms;
using CommandToTranslate.Core;
using CommandToTranslate.Hooks;
using CommandToTranslate.Services;
using CommandToTranslate.UI;

namespace CommandToTranslate;

/// <summary>
/// Application entry point. Wires all components together and manages the application lifecycle.
/// </summary>
static class Program
{
    private static readonly CancellationTokenSource AppCancellation = new();
    private static AppState? _state;
    private static TranslationService? _translationService;
    private static OnDemandTranslationCoordinator? _translationCoordinator;
    private static HotkeyManager? _hotkeyManager;
    private static TrayIcon? _trayIcon;
    private static KeyboardHook? _keyboardHook;
    private static BufferManager? _bufferManager;

    [STAThread]
    static void Main()
    {
        // Initialize logger first
        Logger.LogStartup();

        // Set up global exception handlers
        Application.ThreadException += (s, e) =>
        {
            Logger.Error("Unhandled thread exception", e.Exception);
            MessageBox.Show(
                $"Error: {e.Exception.Message}\n\nLog file: {Logger.LogFilePath}",
                "command-to-translate Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Error("Unhandled domain exception", ex);
        };

        // Set up application
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            Logger.Info("Loading configuration...");

            // Step 1: Load configuration
            var config = AppConfig.Load();
            Logger.Info($"Configuration loaded. Ollama endpoint: {config.Ollama.Endpoint}");

            // Step 2: Create AppState with config
            _state = new AppState { Config = config };
            Logger.Info("AppState created");

            // Step 3: Create all services
            _translationService = new TranslationService(_state);
            var focusContextService = new FocusContextService();
            var clipboardService = new ClipboardService();
            var inputDispatcher = new InputDispatcher(_state);

            // Keystroke buffer: captures typed text for TUI terminals where
            // clipboard-based select+copy is impossible.
            _bufferManager = new BufferManager();
            _keyboardHook = new KeyboardHook(_state, _bufferManager);
            _keyboardHook.Start();
            Logger.Info("KeyboardHook and BufferManager started");

            var adapters = new ITranslationTargetAdapter[]
            {
                new WindowsTerminalLineAdapter(),
                new ClassicConsoleLineAdapter(),
                new ElectronTerminalAdapter(),
                new GenericTextFieldAdapter()
            };
            _translationCoordinator = new OnDemandTranslationCoordinator(
                _state,
                _translationService,
                focusContextService,
                clipboardService,
                inputDispatcher,
                adapters,
                _bufferManager);
            Logger.Info("Services created");

            // Step 4: Create UI - need a message-only window for hotkeys
            _trayIcon = new TrayIcon(_state);
            Logger.Info("TrayIcon created");

            // Create a hidden form for receiving hotkey messages
            using var messageWindow = new MessageWindow();
            _hotkeyManager = new HotkeyManager(_state, messageWindow.Handle);
            Logger.Info("HotkeyManager created");

            // Step 6: Register hotkey
            if (!_hotkeyManager.Register())
            {
                Logger.Warning("Failed to register hotkey");
                _trayIcon.ShowNotification(
                    "command-to-translate",
                    "Failed to register hotkey. It may be in use by another application.",
                    ToolTipIcon.Warning);
            }
            else
            {
                Logger.Info("Hotkey registered successfully");
            }

            // Step 6: Wire events
            _hotkeyManager.HotkeyPressed += async (s, e) =>
            {
                Logger.Info("Hotkey pressed - starting on-demand translation");
                await HandleHotkeyAsync();
            };

            _trayIcon.ExitRequested += (s, e) =>
            {
                Logger.Info("Exit requested from tray");
                AppCancellation.Cancel();
                Application.Exit();
            };

            _trayIcon.ToggleRequested += (s, e) =>
            {
                _state!.IsPaused = !_state.IsPaused;
                _trayIcon!.UpdateIcon();

                var status = _state.IsPaused ? "Disabled" : "Enabled";
                Logger.Info($"Hotkey toggled: {status}");
                _trayIcon.ShowNotification(
                    "command-to-translate",
                    $"Hotkey translation {status}",
                    ToolTipIcon.Info);
            };

            // Hook up message window to hotkey manager
            messageWindow.HotkeyMessageReceived += (msg, wParam) =>
            {
                return _hotkeyManager.ProcessMessage(msg, wParam);
            };

            // Step 7: Health check loop (every 30s)
            _ = StartHealthCheckLoop();

            // Step 8: Initial health check
            _ = PerformInitialHealthCheck();

            // Step 9: Run message loop
            Logger.Info("Starting message loop...");
            Application.Run(messageWindow);

            // Step 10: Cleanup on exit
            Logger.Info("Application exiting, cleaning up...");
            Cleanup();
            Logger.LogShutdown();
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal error in Main", ex);
            MessageBox.Show(
                $"Fatal error: {ex.Message}\n\nLog file: {Logger.LogFilePath}",
                "command-to-translate",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static async Task HandleHotkeyAsync()
    {
        var result = await _translationCoordinator!.ExecuteAsync(AppCancellation.Token);
        if (!result.Success)
        {
            Logger.Warning($"On-demand translation skipped: {result.Message}");

            if (_state!.Config.Ui.NotifyOnError)
            {
                _trayIcon!.ShowNotification(
                    "command-to-translate",
                    result.Message,
                    ToolTipIcon.Warning);
            }
        }
    }

    /// <summary>
    /// Periodic health check to verify Ollama availability (every 30 seconds).
    /// </summary>
    private static async Task StartHealthCheckLoop()
    {
        Logger.Info("Health check loop started");
        while (!AppCancellation.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), AppCancellation.Token);

                var (isHealthy, errorMessage) = await _translationService!.CheckHealthAsync();

                var wasAvailable = _state!.OllamaAvailable;
                _state.OllamaAvailable = isHealthy;

                Logger.Info($"Health check: Ollama {(isHealthy ? "available" : "unavailable")}");

                // Update tray icon if availability changed
                if (wasAvailable != isHealthy)
                {
                    _trayIcon!.UpdateIcon();

                    if (!isHealthy && _state.Config.Ui.NotifyOnError)
                    {
                        _trayIcon.ShowNotification(
                            "command-to-translate - Error",
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
                Logger.Error("Health check error", ex);
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
        Logger.Info("Performing initial health check...");
        try
        {
            var (isHealthy, errorMessage) = await _translationService!.CheckHealthAsync();

            _state!.OllamaAvailable = isHealthy;
            _trayIcon!.UpdateIcon();

            Logger.Info($"Initial health check result: {(isHealthy ? "OK" : errorMessage)}");

            if (!isHealthy && _state.Config.Ui.NotifyOnError)
            {
                _trayIcon.ShowNotification(
                    "command-to-translate - Startup Error",
                    errorMessage ?? "Ollama unavailable. Please start Ollama.",
                    ToolTipIcon.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Initial health check error", ex);
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
        _bufferManager?.Dispose();
        _hotkeyManager?.Dispose();
        _trayIcon?.Dispose();
        _translationService?.Dispose();
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
