using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DayNote.Core.Configuration;
using DayNote.Core.Time;

namespace DayNote.Desktop.Views;

/// <summary>The custom settings dialog. Edits a working copy of <see cref="AppConfig"/> in place when applied.</summary>
public sealed class SettingsDialog : DialogBase
{
    private readonly AppConfig _config;
    private readonly AppConfig _original;
    private readonly ComboBox _fontCombo;
    private readonly TextBox _addFontBox;
    private readonly NumericUpDown _fontSize;
    private readonly NumericUpDown _autosave;
    private readonly TextBox _timeZone;
    private readonly NumericUpDown _backupThrottle;
    private readonly Button _saveButton;

    public SettingsDialog(AppConfig config)
    {
        _config = config;
        _original = config.Copy();
        Title = "Settings";
        Width = 460;

        _fontCombo = new ComboBox
        {
            ItemsSource = config.EditorFonts,
            SelectedItem = config.EditorFont,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _addFontBox = new TextBox { PlaceholderText = "Add a font family name" };
        var addButton = new Button { Content = "Add" };
        addButton.Classes.Add("utility");
        addButton.Click += (_, _) =>
        {
            var name = _addFontBox.Text?.Trim();
            if (!string.IsNullOrEmpty(name) && !config.EditorFonts.Contains(name))
            {
                config.EditorFonts.Add(name);
                _fontCombo.ItemsSource = null;
                _fontCombo.ItemsSource = config.EditorFonts;
                _fontCombo.SelectedItem = name;
                _addFontBox.Text = string.Empty;
                Revalidate();
            }
        };

        _fontSize = Numeric(8, 48, 1, (decimal)config.EditorFontSize);
        _autosave = Numeric(0.25m, 60m, 0.25m, (decimal)config.AutosaveDelaySeconds);
        _timeZone = new TextBox { Text = config.DisplayTimeZone };
        _backupThrottle = Numeric(1, 86400, 30, config.BackupThrottleSeconds);

        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_addFontBox, 0);
        Grid.SetColumn(addButton, 1);
        addButton.Margin = new Avalonia.Thickness(8, 0, 0, 0);
        addRow.Children.Add(_addFontBox);
        addRow.Children.Add(addButton);

        var panel = new StackPanel { Spacing = 8, Width = 410 };
        panel.Children.Add(Label("Editor font"));
        panel.Children.Add(_fontCombo);
        panel.Children.Add(addRow);
        panel.Children.Add(Label("Font size"));
        panel.Children.Add(_fontSize);
        panel.Children.Add(Label("Autosave delay (seconds)"));
        panel.Children.Add(_autosave);
        panel.Children.Add(Label("Display time zone (IANA id, e.g. Asia/Tokyo)"));
        panel.Children.Add(_timeZone);
        panel.Children.Add(Label("Backup interval (seconds)"));
        panel.Children.Add(_backupThrottle);

        SetContent(panel);
        var buttons = SetButtons([("Cancel", "cancel", false), ("Save", "ok", true)]);
        _saveButton = buttons["ok"];

        // Save commits a draft, so it stays disabled until the user makes a valid change. Focus the
        // first control rather than the (initially disabled) Save button.
        _fontCombo.SelectionChanged += (_, _) => Revalidate();
        _fontSize.ValueChanged += (_, _) => Revalidate();
        _autosave.ValueChanged += (_, _) => Revalidate();
        _timeZone.TextChanged += (_, _) => Revalidate();
        _backupThrottle.ValueChanged += (_, _) => Revalidate();
        Revalidate();
        SetInitialFocus(_fontCombo);
    }

    public bool Applied => ResultTag == "ok";

    /// <summary>Copies the edited values back into the working config. Only called when Save was enabled, so inputs are valid.</summary>
    public void ApplyToConfig()
    {
        if (_fontCombo.SelectedItem is string font && !string.IsNullOrWhiteSpace(font))
        {
            _config.EditorFont = font;
        }

        _config.EditorFontSize = (double)(_fontSize.Value ?? (decimal)_config.EditorFontSize);
        _config.AutosaveDelaySeconds = (double)(_autosave.Value ?? (decimal)_config.AutosaveDelaySeconds);
        if (!string.IsNullOrWhiteSpace(_timeZone.Text))
        {
            _config.DisplayTimeZone = _timeZone.Text.Trim();
        }

        _config.BackupThrottleSeconds = (int)(_backupThrottle.Value ?? _config.BackupThrottleSeconds);
    }

    /// <summary>Enables Save only when the draft is both valid and changed from the values the dialog opened with.</summary>
    private void Revalidate() => _saveButton.IsEnabled = IsValid() && IsDirty();

    private bool IsValid() =>
        _fontCombo.SelectedItem is string font && !string.IsNullOrWhiteSpace(font) &&
        InRange(_fontSize) &&
        InRange(_autosave) &&
        InRange(_backupThrottle) &&
        DayNoteTime.TryResolveTimeZone((_timeZone.Text ?? string.Empty).Trim(), out _);

    private bool IsDirty() =>
        (_fontCombo.SelectedItem as string) != _original.EditorFont ||
        !_config.EditorFonts.SequenceEqual(_original.EditorFonts) ||
        _fontSize.Value != (decimal)_original.EditorFontSize ||
        _autosave.Value != (decimal)_original.AutosaveDelaySeconds ||
        _backupThrottle.Value != _original.BackupThrottleSeconds ||
        (_timeZone.Text ?? string.Empty).Trim() != _original.DisplayTimeZone;

    private static bool InRange(NumericUpDown control) =>
        control.Value is { } value && value >= control.Minimum && value <= control.Maximum;

    private static NumericUpDown Numeric(decimal min, decimal max, decimal increment, decimal value) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = increment,
        Value = value,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };
}
