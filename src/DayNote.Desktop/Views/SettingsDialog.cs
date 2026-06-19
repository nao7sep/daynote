using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DayNote.Core.Configuration;
using DayNote.Core.Time;

namespace DayNote.Desktop.Views;

/// <summary>
/// The custom settings dialog. Edits a working copy of <see cref="AppConfig"/> in place: controls
/// write straight to that copy, so the caller applies the edits by keeping the copy on Save and
/// discards it on Cancel. Every text-style preset is an independent editable card.
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
    private readonly StackPanel _styleItems = new() { Spacing = 8 };
    private readonly Dictionary<EditorTextStyle, StyleEditorControls> _styleEditors = [];
    private readonly Button _removeStyle;
    private readonly NumericUpDown _autosave;
    private readonly TextBox _timeZone;
    private readonly Button _saveButton;
    private EditorTextStyle? _activeStyle;

    public SettingsDialog(AppConfig config)
    {
        _config = config;
        _original = config.Copy();
        Title = "Settings";
        Width = 600;

        var addStyle = Utility("Add", AddStyle);
        _removeStyle = Utility("Remove", RemoveStyle);

        var styleActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        styleActions.Children.Add(addStyle);
        styleActions.Children.Add(_removeStyle);

        var styleHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        styleHeader.Children.Add(Label("Text styles"));
        Grid.SetColumn(styleActions, 1);
        styleHeader.Children.Add(styleActions);

        var styleScroller = new ScrollViewer
        {
            Height = 320,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _styleItems,
        };

        _autosave = Numeric(0.25m, 60m, 0.25m);
        _autosave.Value = (decimal)config.AutosaveDelaySeconds;
        _timeZone = new TextBox { Text = config.DisplayTimeZone };

        var panel = new StackPanel { Spacing = 8, Width = 540 };
        panel.Children.Add(styleHeader);
        panel.Children.Add(styleScroller);
        panel.Children.Add(Label("Autosave delay (seconds)"));
        panel.Children.Add(_autosave);
        panel.Children.Add(Label("Display time zone (IANA id, e.g. Asia/Tokyo)"));
        panel.Children.Add(_timeZone);

        SetContent(panel);
        var buttons = SetButtons([("Cancel", "cancel", false), ("Save", "ok", true)]);
        _saveButton = buttons["ok"];

        var selected = _config.TextStyles
            .FirstOrDefault(s => string.Equals(s.Name, _config.SelectedTextStyle, StringComparison.OrdinalIgnoreCase))
            ?? _config.TextStyles.FirstOrDefault();
        RebuildStyleCards(selected);

        _autosave.ValueChanged += (_, _) =>
        {
            if (_autosave.Value is { } value)
                _config.AutosaveDelaySeconds = (double)value;
            Revalidate();
        };
        _timeZone.TextChanged += (_, _) =>
        {
            _config.DisplayTimeZone = (_timeZone.Text ?? string.Empty).Trim();
            Revalidate();
        };

        Revalidate();
        if (_activeStyle is not null && _styleEditors.TryGetValue(_activeStyle, out var editor))
            SetInitialFocus(editor.ActiveToggle);
    }

    public bool Applied => ResultTag == "ok";

    private Border BuildStyleCard(EditorTextStyle style)
    {
        var heading = new TextBlock
        {
            Text = DisplayName(style.Name),
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
        };
        var active = new RadioButton
        {
            Content = heading,
            GroupName = "TextStylePresets",
            IsChecked = ReferenceEquals(style, _activeStyle),
        };

        var name = new TextBox { Text = style.Name, PlaceholderText = "Preset name" };
        var fontFamily = new TextBox { Text = style.FontFamily, PlaceholderText = "Font family (e.g. Menlo)" };
        var fontSize = Numeric((decimal)MinFontSize, (decimal)MaxFontSize, 1);
        fontSize.Value = (decimal)style.FontSize;
        var lineSpacing = Numeric((decimal)MinLineSpacing, (decimal)MaxLineSpacing, 0.1m);
        lineSpacing.Value = (decimal)style.LineSpacing;
        var padding = Numeric((decimal)MinPadding, (decimal)MaxPadding, 1);
        padding.Value = (decimal)style.Padding;
        var bold = new CheckBox { Content = "Bold", IsChecked = style.Bold };
        var italic = new CheckBox { Content = "Italic", IsChecked = style.Italic };

        var decorations = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        decorations.Children.Add(bold);
        decorations.Children.Add(italic);

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(active);
        body.Children.Add(Row(("*", Field("Name", name)), ("*", Field("Font family", fontFamily))));
        body.Children.Add(Row(
            ("*", Field("Font size", fontSize)),
            ("*", Field("Line spacing", lineSpacing)),
            ("*", Field("Padding", padding))));
        body.Children.Add(decorations);

        var card = new Border { Child = body };
        card.Classes.Add("stylePreset");
        if (ReferenceEquals(style, _activeStyle))
            card.Classes.Add("selected");

        _styleEditors[style] = new StyleEditorControls(card, active, name, fontFamily, fontSize, lineSpacing, padding);

        active.IsCheckedChanged += (_, _) =>
        {
            if (active.IsChecked != true)
                return;

            _activeStyle = style;
            _config.SelectedTextStyle = style.Name;
            UpdateStyleCardSelection();
            Revalidate();
        };
        name.TextChanged += (_, _) =>
        {
            style.Name = (name.Text ?? string.Empty).Trim();
            heading.Text = DisplayName(style.Name);
            if (ReferenceEquals(style, _activeStyle))
                _config.SelectedTextStyle = style.Name;
            Revalidate();
        };
        fontFamily.TextChanged += (_, _) =>
        {
            style.FontFamily = (fontFamily.Text ?? string.Empty).Trim();
            Revalidate();
        };
        fontSize.ValueChanged += (_, _) =>
        {
            if (fontSize.Value is { } value)
                style.FontSize = (double)value;
            Revalidate();
        };
        lineSpacing.ValueChanged += (_, _) =>
        {
            if (lineSpacing.Value is { } value)
                style.LineSpacing = (double)value;
            Revalidate();
        };
        padding.ValueChanged += (_, _) =>
        {
            if (padding.Value is { } value)
                style.Padding = (double)value;
            Revalidate();
        };
        bold.IsCheckedChanged += (_, _) =>
        {
            style.Bold = bold.IsChecked == true;
            Revalidate();
        };
        italic.IsCheckedChanged += (_, _) =>
        {
            style.Italic = italic.IsChecked == true;
            Revalidate();
        };

        return card;
    }

    private void AddStyle()
    {
        var template = _activeStyle ?? _config.TextStyles.FirstOrDefault();
        var style = template?.Copy() ?? new EditorTextStyle();
        style.Name = UniqueName(template is null ? "New style" : template.Name + " copy");
        _config.TextStyles.Add(style);
        RebuildStyleCards(style);
        Revalidate();
    }

    private void RemoveStyle()
    {
        if (_config.TextStyles.Count <= 1 || _activeStyle is not { } style)
            return;

        var index = _config.TextStyles.IndexOf(style);
        _config.TextStyles.Remove(style);
        var next = _config.TextStyles[Math.Min(index, _config.TextStyles.Count - 1)];
        RebuildStyleCards(next);
        Revalidate();
    }

    private void RebuildStyleCards(EditorTextStyle? active)
    {
        _activeStyle = active ?? _config.TextStyles.FirstOrDefault();
        if (_activeStyle is not null)
            _config.SelectedTextStyle = _activeStyle.Name;

        _styleEditors.Clear();
        _styleItems.Children.Clear();
        foreach (var style in _config.TextStyles)
            _styleItems.Children.Add(BuildStyleCard(style));

        _removeStyle.IsEnabled = _config.TextStyles.Count > 1;
    }

    private void UpdateStyleCardSelection()
    {
        foreach (var (style, editor) in _styleEditors)
        {
            editor.Card.Classes.Remove("selected");
            if (ReferenceEquals(style, _activeStyle))
                editor.Card.Classes.Add("selected");
        }
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var counter = 2;
        while (_config.TextStyles.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {counter++}";
        return name;
    }

    private void Revalidate() => _saveButton.IsEnabled = IsValid() && IsDirty();

    private bool IsValid()
    {
        if (!DayNoteTime.TryResolveTimeZone((_timeZone.Text ?? string.Empty).Trim(), out _) || !InRange(_autosave))
            return false;

        if (_config.TextStyles.Count == 0 || _styleEditors.Count != _config.TextStyles.Count)
            return false;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in _config.TextStyles)
        {
            var controls = _styleEditors[style];
            if (string.IsNullOrWhiteSpace(controls.Name.Text)
                || string.IsNullOrWhiteSpace(controls.FontFamily.Text)
                || !InRange(controls.FontSize)
                || !InRange(controls.LineSpacing)
                || !InRange(controls.Padding)
                || !names.Add(style.Name))
            {
                return false;
            }
        }

        return _activeStyle is not null;
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
        var button = new Button { Content = text };
        button.Classes.Add("utility");
        button.Click += (_, _) => onClick();
        return button;
    }

    private static StackPanel Field(string label, Control control)
    {
        var field = new StackPanel { Spacing = 4 };
        field.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 12 });
        field.Children.Add(control);
        return field;
    }

    private static Grid Row(params (string Width, Control Child)[] cells)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", cells.Select(c => c.Width))),
            ColumnSpacing = 8,
        };
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i].Child, i);
            grid.Children.Add(cells[i].Child);
        }
        return grid;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };

    private static string DisplayName(string name) => string.IsNullOrWhiteSpace(name) ? "Unnamed style" : name;

    private sealed record StyleEditorControls(
        Border Card,
        RadioButton ActiveToggle,
        TextBox Name,
        TextBox FontFamily,
        NumericUpDown FontSize,
        NumericUpDown LineSpacing,
        NumericUpDown Padding);
}
