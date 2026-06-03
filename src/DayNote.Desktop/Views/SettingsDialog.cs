using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DayNote.Core.Configuration;

namespace DayNote.Desktop.Views;

/// <summary>The custom settings dialog. Edits a working copy of <see cref="AppConfig"/> in place when applied.</summary>
public sealed class SettingsDialog : DialogBase
{
    private readonly AppConfig _config;
    private readonly ComboBox _fontCombo;
    private readonly TextBox _addFontBox;
    private readonly NumericUpDown _fontSize;
    private readonly NumericUpDown _autosave;
    private readonly TextBox _timeZone;
    private readonly NumericUpDown _backupThrottle;
    private readonly NumericUpDown _searchPageSize;

    public SettingsDialog(AppConfig config)
    {
        _config = config;
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
            }
        };

        _fontSize = Numeric(8, 48, 1, (decimal)config.EditorFontSize);
        _autosave = Numeric(0.25m, 60m, 0.25m, (decimal)config.AutosaveDelaySeconds);
        _timeZone = new TextBox { Text = config.DisplayTimeZone };
        _backupThrottle = Numeric(1, 86400, 30, config.BackupThrottleSeconds);
        _searchPageSize = Numeric(10, 1000, 10, config.SearchPageSize);

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
        panel.Children.Add(Label("Cross-notebook search page size"));
        panel.Children.Add(_searchPageSize);

        SetContent(panel);
        var buttons = SetButtons([("Cancel", "cancel", false), ("Save", "ok", true)]);
        SetInitialFocus(buttons["ok"]);
    }

    public bool Applied => ResultTag == "ok";

    /// <summary>Copies the edited values back into the working config.</summary>
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
        _config.SearchPageSize = (int)(_searchPageSize.Value ?? _config.SearchPageSize);
    }

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
