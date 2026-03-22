// src/UI/TrayIcon.cs
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RealTranslate.Core;

namespace RealTranslate.UI;

/// <summary>
/// System tray icon with context menu for application control.
/// </summary>
public class TrayIcon : IDisposable
{
    private readonly AppState _state;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enableTranslationItem;

    private Icon? _activeIcon;
    private Icon? _pausedIcon;
    private Icon? _errorIcon;

    // Store HICON handles for proper cleanup
    private IntPtr _activeIconHandle;
    private IntPtr _pausedIconHandle;
    private IntPtr _errorIconHandle;

    private bool _disposed;

    // P/Invoke to destroy HICON handles
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public event EventHandler? ExitRequested;
    public event EventHandler? ToggleRequested;

    public TrayIcon(AppState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));

        // Create icons
        CreateIcons();

        // Create context menu
        var contextMenu = new ContextMenuStrip();

        // Enable translation toggle
        _enableTranslationItem = new ToolStripMenuItem("Enable translation", null, OnToggleTranslation)
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
            ContextMenuStrip = contextMenu,
            Visible = true,
            Text = "real-translate"
        };

        // Set initial icon state
        UpdateIcon();
    }

    private void CreateIcons()
    {
        // Create simple colored icons (16x16)
        (_activeIcon, _activeIconHandle) = CreateSolidIcon(Color.FromArgb(0, 180, 0));    // Green
        (_pausedIcon, _pausedIconHandle) = CreateSolidIcon(Color.FromArgb(160, 160, 160)); // Gray
        (_errorIcon, _errorIconHandle) = CreateSolidIcon(Color.FromArgb(220, 0, 0));     // Red
    }

    private static (Icon icon, IntPtr handle) CreateSolidIcon(Color color)
    {
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);

        // Fill with the color
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, 0, 0, 16, 16);

        // Add a subtle border
        using var pen = new Pen(Color.FromArgb(100, Color.Black), 1);
        graphics.DrawRectangle(pen, 0, 0, 15, 15);

        // Convert to icon - store handle for cleanup
        var hIcon = bitmap.GetHicon();
        return (Icon.FromHandle(hIcon), hIcon);
    }

    public void UpdateIcon()
    {
        if (_disposed)
            return;

        Icon? icon;
        string text;

        if (!_state.OllamaAvailable)
        {
            icon = _errorIcon;
            text = "real-translate: Error - Ollama unavailable";
        }
        else if (_state.IsPaused)
        {
            icon = _pausedIcon;
            text = "real-translate: Paused";
        }
        else
        {
            icon = _activeIcon;
            text = "real-translate: Active";
        }

        _notifyIcon.Icon = icon;
        _notifyIcon.Text = text;

        // Update menu item state
        _enableTranslationItem.Checked = !_state.IsPaused;
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon)
    {
        if (_disposed)
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
            "real-translate v1.0.0\n\n" +
            "A real-time translation tool using Ollama.\n\n" +
            "Usage:\n" +
            "1. Select text to translate\n" +
            "2. Press Ctrl+Shift+T\n" +
            "3. Translation replaces selection\n\n" +
            "Requires Ollama with a translation model.",
            "About real-translate",
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

        // Dispose icons and destroy HICON handles
        _activeIcon?.Dispose();
        _pausedIcon?.Dispose();
        _errorIcon?.Dispose();

        if (_activeIconHandle != IntPtr.Zero)
            DestroyIcon(_activeIconHandle);
        if (_pausedIconHandle != IntPtr.Zero)
            DestroyIcon(_pausedIconHandle);
        if (_errorIconHandle != IntPtr.Zero)
            DestroyIcon(_errorIconHandle);

        GC.SuppressFinalize(this);
    }
}
