using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using DayNote.Core.Configuration;
using DayNote.Core.Time;

namespace DayNote.Desktop.Views;

/// <summary>
/// The custom settings dialog. Edits a working copy of <see cref="AppConfig"/> in place: controls
/// write straight to that copy, so the caller applies the edits simply by keeping the copy when Save
/// is pressed and discarding it on Cancel. The editor-appearance section manages named text-style
/// presets (the combo selects the active preset and the fields below edit it).
/// </summary>
public sealed class SettingsDialog : DialogBase
{
    private const double MinFontSize = 8;
    private const double MaxFontSize = 48;
    private const double MinLineSpacing = 1.0;
    private const double MaxLineSpacing = 3.0;
    private const double MinPadding = 0;
    private const double MaxPadding = 48;

    private readonly AppConfig _config;
    private readonly AppConfig _original;

    private readonly ComboBox _styleCombo;
    private readonly TextBox _styleName;
    private readonly TextBox _fontFamily;
    private readonly NumericUpDown _fontSize;
    private readonly NumericUpDown _lineSpacing;
    private readonly NumericUpDown _padding;
    private readonly CheckBox _bold;
    private readonly CheckBox _italic;
    private readonly Button _removeStyle;
    private readonly NumericUpDown _autosave;
    private readonly TextBox _timeZone;
    private readonly Button _saveButton;

    // Suppresses the field-change handlers while a preset's values are being loaded into the controls.
    private bool _loading;

    public SettingsDialog(AppConfig config)
    {
        _config = config;
        _original = config.Copy();
        Title = "Settings";
        Width = 460;

        _styleCombo = new ComboBox
        {
            ItemsSource = _config.TextStyles,
            DisplayMemberBinding = new Binding(nameof(EditorTextStyle.Name)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var addStyle = Utility("Add", AddStyle);
        _removeStyle = Utility("Remove", RemoveStyle);

        _styleName = new TextBox { PlaceholderText = "Preset name" };
        _fontFamily = new TextBox { PlaceholderText = "Font family (e.g. Menlo)" };
        _fontSize = Numeric((decimal)MinFontSize, (decimal)MaxFontSize, 1);
        _lineSpacing = Numeric((decimal)MinLineSpacing, (decimal)MaxLineSpacing, 0.1m);
        _padding = Numeric((decimal)MinPadding, (decimal)MaxPadding, 1);
        _bold = new CheckBox { Content = "Bold" };
        _italic = new CheckBox { Content = "Italic" };

        _autosave = Numeric(0.25m, 60m, 0.25m);
        _autosave.Value = (decimal)config.AutosaveDelaySeconds;
        _timeZone = new TextBox { Text = config.DisplayTimeZone };

        var styleRow = Row(("*", _styleCombo), ("Auto", addStyle), ("Auto", _removeStyle));
        var decorationRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        decorationRow.Children.Add(_bold);
        decorationRow.Children.Add(_italic);

        var panel = new StackPanel { Spacing = 8, Width = 410 };
        panel.Children.Add(Label("Text style"));
        panel.Children.Add(styleRow);
        panel.Children.Add(Label("Name"));
        panel.Children.Add(_styleName);
        panel.Children.Add(Label("Font family"));
        panel.Children.Add(_fontFamily);
        panel.Children.Add(Label("Font size"));
        panel.Children.Add(_fontSize);
        panel.Children.Add(Label("Line spacing (× font size)"));
        panel.Children.Add(_lineSpacing);
        panel.Children.Add(Label("Padding"));
        panel.Children.Add(_padding);
        panel.Children.Add(decorationRow);
        panel.Children.Add(Label("Autosave delay (seconds)"));
        panel.Children.Add(_autosave);
        panel.Children.Add(Label("Display time zone (IANA id, e.g. Asia/Tokyo)"));
        panel.Children.Add(_timeZone);

        SetContent(panel);
        var buttons = SetButtons([("Cancel", "cancel", false), ("Save", "ok", true)]);
        _saveButton = buttons["ok"];

        _styleCombo.SelectedItem = _config.TextStyles
            .FirstOrDefault(s => string.Equals(s.Name, _config.SelectedTextStyle, StringComparison.OrdinalIgnoreCase))
            ?? _config.TextStyles.FirstOrDefault();
        LoadSelectedStyle();

        _styleCombo.SelectionChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            if (Selected is { } s)
            {
                _config.SelectedTextStyle = s.Name;
            }

            LoadSelectedStyle();
            Revalidate();
        };

        _styleName.TextChanged += (_, _) => EditSelected(s => { s.Name = (_styleName.Text ?? string.Empty).Trim(); _config.SelectedTextStyle = s.Name; });
        // Re-render the combo's labels once the rename is committed (a plain object doesn't notify).
        _styleName.LostFocus += (_, _) => { if (Selected is { } s) RebuildCombo(s); };
        _fontFamily.TextChanged += (_, _) => EditSelected(s => s.FontFamily = (_fontFamily.Text ?? string.Empty).Trim());
        _fontSize.ValueChanged += (_, _) => EditSelected(s => { if (_fontSize.Value is { } v) s.FontSize = (double)v; });
        _lineSpacing.ValueChanged += (_, _) => EditSelected(s => { if (_lineSpacing.Value is { } v) s.LineSpacing = (double)v; });
        _padding.ValueChanged += (_, _) => EditSelected(s => { if (_padding.Value is { } v) s.Padding = (double)v; });
        _bold.IsCheckedChanged += (_, _) => EditSelected(s => s.Bold = _bold.IsChecked == true);
        _italic.IsCheckedChanged += (_, _) => EditSelected(s => s.Italic = _italic.IsChecked == true);

        _autosave.ValueChanged += (_, _) => { if (_autosave.Value is { } v) _config.AutosaveDelaySeconds = (double)v; Revalidate(); };
        _timeZone.TextChanged += (_, _) => { _config.DisplayTimeZone = (_timeZone.Text ?? string.Empty).Trim(); Revalidate(); };

        Revalidate();
        SetInitialFocus(_styleCombo);
    }

    public bool Applied => ResultTag == "ok";

    private EditorTextStyle? Selected => _styleCombo.SelectedItem as EditorTextStyle;

    /// <summary>Loads the selected preset's values into the editing controls without firing their handlers.</summary>
    private void LoadSelectedStyle()
    {
        var style = Selected;
        _loading = true;
        _styleName.Text = style?.Name ?? string.Empty;
        _fontFamily.Text = style?.FontFamily ?? string.Empty;
        _fontSize.Value = style is null ? null : (decimal)style.FontSize;
        _lineSpacing.Value = style is null ? null : (decimal)style.LineSpacing;
        _padding.Value = style is null ? null : (decimal)style.Padding;
        _bold.IsChecked = style?.Bold ?? false;
        _italic.IsChecked = style?.Italic ?? false;
        _removeStyle.IsEnabled = _config.TextStyles.Count > 1;
        _loading = false;
    }

    private void EditSelected(Action<EditorTextStyle> edit)
    {
        if (_loading || Selected is not { } style)
        {
            return;
        }

        edit(style);
        Revalidate();
    }

    private void AddStyle()
    {
        var template = Selected ?? _config.TextStyles.FirstOrDefault();
        var style = template?.Copy() ?? new EditorTextStyle();
        style.Name = UniqueName(template is null ? "New style" : template.Name + " copy");
        _config.TextStyles.Add(style);
        RebuildCombo(style);
        Revalidate();
    }

    private void RemoveStyle()
    {
        if (_config.TextStyles.Count <= 1 || Selected is not { } style)
        {
            return;
        }

        _config.TextStyles.Remove(style);
        RebuildCombo(_config.TextStyles.FirstOrDefault());
        Revalidate();
    }

    /// <summary>Rebuilds the combo (so renamed labels re-render) and re-selects the given preset.</summary>
    private void RebuildCombo(EditorTextStyle? select)
    {
        _loading = true;
        _styleCombo.ItemsSource = null;
        _styleCombo.ItemsSource = _config.TextStyles;
        _styleCombo.SelectedItem = select ?? _config.TextStyles.FirstOrDefault();
        _loading = false;

        if (Selected is { } s)
        {
            _config.SelectedTextStyle = s.Name;
        }

        LoadSelectedStyle();
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var counter = 2;
        while (_config.TextStyles.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName} {counter}";
            counter++;
        }

        return name;
    }

    private void Revalidate() => _saveButton.IsEnabled = IsValid() && IsDirty();

    private bool IsValid()
    {
        if (!DayNoteTime.TryResolveTimeZone((_timeZone.Text ?? string.Empty).Trim(), out _) || !InRange(_autosave))
        {
            return false;
        }

        // The live controls for the selected preset must be complete and in range.
        if (string.IsNullOrWhiteSpace(_styleName.Text) || string.IsNullOrWhiteSpace(_fontFamily.Text)
            || !InRange(_fontSize) || !InRange(_lineSpacing) || !InRange(_padding))
        {
            return false;
        }

        // Every preset must be self-consistent and uniquely named (covers presets edited then switched away from).
        if (_config.TextStyles.Count == 0)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _config.TextStyles)
        {
            if (string.IsNullOrWhiteSpace(s.Name) || !names.Add(s.Name.Trim()) || string.IsNullOrWhiteSpace(s.FontFamily)
                || s.FontSize is < MinFontSize or > MaxFontSize
                || s.LineSpacing is < MinLineSpacing or > MaxLineSpacing
                || s.Padding is < MinPadding or > MaxPadding)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsDirty() => Serialize(_config) != Serialize(_original);

    private static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, DayNoteJson.Options);

    private static bool InRange(NumericUpDown control) =>
        control.Value is { } value && value >= control.Minimum && value <= control.Maximum;

    private static NumericUpDown Numeric(decimal min, decimal max, decimal increment) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = increment,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static Button Utility(string text, Action onClick)
    {
        var button = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0) };
        button.Classes.Add("utility");
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Grid Row(params (string Width, Control Child)[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", cells.Select(c => c.Width))) };
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i].Child, i);
            grid.Children.Add(cells[i].Child);
        }

        return grid;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };
}
