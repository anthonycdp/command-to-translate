using System.Drawing;
using System.Windows.Forms;
using CommandToTranslate.Core;

namespace CommandToTranslate.UI;

/// <summary>
/// Modal dialog for selecting source and target translation languages.
/// </summary>
public sealed class LanguageSelectionForm : Form
{
    private readonly ComboBox _sourceComboBox = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };

    private readonly ComboBox _targetComboBox = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
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

    public LanguageSelectionForm(
        IReadOnlyList<TranslationLanguage> supportedLanguages,
        TranslationPair activePair)
    {
        ArgumentNullException.ThrowIfNull(supportedLanguages);
        ArgumentNullException.ThrowIfNull(activePair);

        Text = "Select Translation Languages";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(360, 0);
        Padding = new Padding(12);

        BuildLayout();

        foreach (var language in supportedLanguages)
        {
            _sourceComboBox.Items.Add(language);
            _targetComboBox.Items.Add(language);
        }

        SelectLanguage(_sourceComboBox, activePair.SourceLanguage.Code);
        SelectLanguage(_targetComboBox, activePair.TargetLanguage.Code);

        _sourceComboBox.SelectedIndexChanged += (_, _) => UpdateValidationState();
        _targetComboBox.SelectedIndexChanged += (_, _) => UpdateValidationState();

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;

        UpdateValidationState();
    }

    public string SelectedSourceLanguageCode =>
        (_sourceComboBox.SelectedItem as TranslationLanguage)?.Code ?? string.Empty;

    public string SelectedTargetLanguageCode =>
        (_targetComboBox.SelectedItem as TranslationLanguage)?.Code ?? string.Empty;

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            RowCount = 4
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(CreateLabel("From"), 0, 0);
        layout.Controls.Add(_sourceComboBox, 1, 0);
        layout.Controls.Add(CreateLabel("To"), 0, 1);
        layout.Controls.Add(_targetComboBox, 1, 1);
        layout.Controls.Add(_validationLabel, 0, 2);
        layout.SetColumnSpan(_validationLabel, 2);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_saveButton);

        layout.Controls.Add(buttonPanel, 0, 3);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 8, 0),
            Text = text
        };
    }

    private void SelectLanguage(ComboBox comboBox, string languageCode)
    {
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is TranslationLanguage language &&
                string.Equals(language.Code, languageCode, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    private void UpdateValidationState()
    {
        var hasValidSelection =
            !string.IsNullOrWhiteSpace(SelectedSourceLanguageCode) &&
            !string.IsNullOrWhiteSpace(SelectedTargetLanguageCode) &&
            !string.Equals(SelectedSourceLanguageCode, SelectedTargetLanguageCode, StringComparison.OrdinalIgnoreCase);

        _saveButton.Enabled = hasValidSelection;
        _validationLabel.Text = hasValidSelection
            ? string.Empty
            : "Source and target languages must be different.";
    }
}
