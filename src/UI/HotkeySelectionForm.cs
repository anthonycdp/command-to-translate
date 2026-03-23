using System.Drawing;
using System.Windows.Forms;
using CommandToTranslate.Core;

namespace CommandToTranslate.UI;

/// <summary>
/// Modal dialog that captures a new hotkey combination from the keyboard.
/// </summary>
public sealed class HotkeySelectionForm : Form
{
    private readonly TextBox _captureTextBox = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true
    };

    private readonly Label _validationLabel = new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill,
        ForeColor = Color.Firebrick
    };

    private readonly Button _saveButton = new()
    {
        Text = "Save",
        DialogResult = DialogResult.OK
    };

    private readonly Button _cancelButton = new()
    {
        Text = "Cancel",
        DialogResult = DialogResult.Cancel
    };

    private HotkeyBinding _selectedBinding;

    public HotkeySelectionForm(HotkeyBinding activeBinding)
    {
        _selectedBinding = activeBinding ?? throw new ArgumentNullException(nameof(activeBinding));

        Text = "Change Hotkey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(420, 0);
        Padding = new Padding(12);

        BuildLayout();
        UpdatePreview(activeBinding, string.Empty);

        KeyDown += OnHotkeyKeyDown;
        _captureTextBox.KeyDown += OnHotkeyKeyDown;
        Shown += (_, _) => _captureTextBox.Focus();
    }

    public HotkeyBinding SelectedBinding => _selectedBinding;

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 5
        };

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Press the new shortcut. It must include at least one modifier key."
        }, 0, 0);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 6),
            Text = "Captured hotkey"
        }, 0, 1);

        layout.Controls.Add(_captureTextBox, 0, 2);
        layout.Controls.Add(_validationLabel, 0, 3);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0),
            WrapContents = false
        };

        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_saveButton);

        layout.Controls.Add(buttonPanel, 0, 4);
        Controls.Add(layout);
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        if (HotkeyBindingParser.TryCreateFromKeyEvent(e, out var binding))
        {
            _selectedBinding = binding;
            UpdatePreview(binding, string.Empty);
            return;
        }

        UpdatePreview(
            _selectedBinding,
            "Use at least one modifier (Ctrl, Shift, Alt or Win) plus a supported key.");
    }

    private void UpdatePreview(HotkeyBinding binding, string validationMessage)
    {
        _captureTextBox.Text = binding.Label;
        _validationLabel.Text = validationMessage;
        _saveButton.Enabled = string.IsNullOrWhiteSpace(validationMessage);
    }
}
