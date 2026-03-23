// src/UI/TrayIcon.cs
using System.Drawing;
using System.Windows.Forms;
using CommandToTranslate.Core;

namespace CommandToTranslate.UI;

/// <summary>
/// System tray icon with context menu for application control.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly AppState _state;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enableTranslationItem;
    private readonly Icon _appIcon;

    private bool _disposed;

    public event EventHandler? ExitRequested;
    public event EventHandler? ToggleRequested;

    public TrayIcon(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));

        // Load icon from embedded resource
        _appIcon = LoadEmbeddedIcon();

        // Create context menu
        var contextMenu = new ContextMenuStrip();

        // Enable translation toggle
        _enableTranslationItem = new ToolStripMenuItem("Enable hotkey translation", null, OnToggleTranslation)
        {
            Checked = !_state.IsPaused
        };
        contextMenu.Items.Add(_enableTranslationItem);

        // Separator
        contextMenu.Items.Add(new ToolStripSeparator());

        // Open config file
        contextMenu.Items.Add("Open config file", null, OnOpenConfig);

        // Separator
        contextMenu.Items.Add(new ToolStripSeparator());

        // About
        contextMenu.Items.Add("About", null, OnAbout);

        // Exit
        contextMenu.Items.Add("Exit", null, OnExit);

        // Create notify icon
        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon,
            ContextMenuStrip = contextMenu,
            Visible = true,
            Text = "command-to-translate"
        };

        // Set initial tooltip
        UpdateIcon();
    }

    private static Icon LoadEmbeddedIcon()
    {
        var assembly = typeof(TrayIcon).Assembly;
        using var stream = assembly.GetManifestResourceStream("CommandToTranslate.icon.ico");
        if (stream == null)
            throw new InvalidOperationException("Embedded icon resource not found: CommandToTranslate.icon.ico");
        return new Icon(stream);
    }

    public void UpdateIcon()
    {
        if (_disposed)
            return;

        string text;

        if (!_state.OllamaAvailable)
        {
            text = "command-to-translate: Error - Ollama unavailable";
        }
        else if (_state.IsPaused)
        {
            text = "command-to-translate: Hotkey disabled";
        }
        else
        {
            text = "command-to-translate: Hotkey ready";
        }

        _notifyIcon.Text = text;

        // Update menu item state
        _enableTranslationItem.Checked = !_state.IsPaused;
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon)
    {
        if (_disposed)
            return;

        if (!_state.Config.Ui.ShowNotifications)
            return;

        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    private void OnToggleTranslation(object? sender, EventArgs e)
    {
        ToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenConfig(object? sender, EventArgs e)
    {
        try
        {
            var configPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "config.toml");

            // Ensure config file exists
            if (!File.Exists(configPath))
            {
                _state.Config.Save();
            }

            // Open with system default editor
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowNotification("Error", $"Failed to open config: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void OnAbout(object? sender, EventArgs e)
    {
        MessageBox.Show(
            "command-to-translate v1.0.0\n\n" +
            "An on-demand translation tool using Ollama.\n\n" +
            "Usage:\n" +
            "1. Type or review your text\n" +
            "2. Press Ctrl+Shift+T\n" +
            "3. The current field or terminal line is replaced with the translation\n\n" +
            "Requires Ollama with a translation model.",
            "About command-to-translate",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _appIcon.Dispose();

        GC.SuppressFinalize(this);
    }
}
