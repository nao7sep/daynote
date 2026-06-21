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
/// discards it on Cancel. Every text-style preset is an independent editable card; exactly one is the
/// default and the default cannot be removed, which guarantees at least one preset always remains.
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
    private readonly NumericUpDown _autosave;
    private readonly TextBox _timeZone;
    private readonly Button _saveButton;
    private EditorTextStyle? _defaultStyle;

    public SettingsDialog(AppConfig config)
    {
        _config = config;
        Title = "Settings";
        Width = 600;

        var styleActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        styleActions.Children.Add(Utility("Add", AddStyle));

        var styleHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var styleLabel = Label("Text styles");
        styleLabel.VerticalAlignment = VerticalAlignment.Bottom; // sit on the baseline of the taller Add button
        styleLabel.Margin = new Thickness(0, 0, 0, 4);
        styleHeader.Children.Add(styleLabel);
        Grid.SetColumn(styleActions, 1);
        styleHeader.Children.Add(styleActions);

        // The presets live in a bordered, padded list container; the cards are its rows.
        var styleList = new Border
        {
            BorderBrush = PaletteBrush.Resolve("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = new ScrollViewer
            {
                Height = 320,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = _styleItems,
            },
        };

        _autosave = Numeric(0.25m, 60m, 0.25m);
        _autosave.Value = (decimal)config.AutosaveDelaySeconds;
        _timeZone = new TextBox { Text = config.DisplayTimeZone };

        var panel = new StackPanel { Spacing = 8, Width = 540 };
        panel.Children.Add(styleHeader);
        panel.Children.Add(styleList);
        panel.Children.Add(Label("Autosave delay (seconds)"));
        panel.Children.Add(_autosave);
        panel.Children.Add(Label("Display time zone (IANA id, e.g. Asia/Tokyo)"));
        panel.Children.Add(_timeZone);

        SetContent(panel);
        var buttons = SetButtons([new DialogButton("Cancel", "cancel"), new DialogButton("Save", "ok", DialogButtonKind.Primary)]);
        _saveButton = buttons["ok"];

        RebuildStyleCards(_config.ResolveSelectedStyle());

        // Snapshot the baseline AFTER the load canonicalizes SelectedTextStyle to the resolved preset's
        // name; otherwise opening the dialog and changing nothing could leave Save enabled (phantom dirt).
        _original = _config.Copy();

        _autosave.ValueChanged += (_, _) =>
        {
            if (_autosave.Value is { } value)
            {
                _config.AutosaveDelaySeconds = (double)value;
            }

            Revalidate();
        };
        _timeZone.TextChanged += (_, _) =>
        {
            _config.DisplayTimeZone = (_timeZone.Text ?? string.Empty).Trim();
            Revalidate();
        };

        Revalidate();
        if (_defaultStyle is not null && _styleEditors.TryGetValue(_defaultStyle, out var editor))
        {
            SetInitialFocus(editor.Name);
        }
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

        // The default control sits bottom-right: a "Set as default" button that becomes a colored
        // "Default" pill once this preset is the chosen one (a pill reads better than plain text here).
        var setDefault = Utility("Set as default", () => MakeDefault(style));
        var defaultLabel = new Border
        {
            Child = new TextBlock
            {
                Text = "Default",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = PaletteBrush.Resolve("AccentForegroundBrush"),
            },
        };
        defaultLabel.Classes.Add("pill");
        var remove = Utility("Remove", () => RemoveStyle(style));

        var decorations = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };
        decorations.Children.Add(bold);
        decorations.Children.Add(italic);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        actions.Children.Add(setDefault);
        actions.Children.Add(defaultLabel);
        actions.Children.Add(remove);

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        bottom.Children.Add(decorations);
        Grid.SetColumn(actions, 1);
        bottom.Children.Add(actions);

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(heading);
        body.Children.Add(Row(("*", Field("Name", name)), ("*", Field("Font family", fontFamily))));
        body.Children.Add(Row(
            ("*", Field("Font size", fontSize)),
            ("*", Field("Line spacing", lineSpacing)),
            ("*", Field("Padding", padding))));
        body.Children.Add(bottom);

        var card = new Border { Child = body };
        card.Classes.Add("stylePreset");

        _styleEditors[style] = new StyleEditorControls(
            name, fontFamily, fontSize, lineSpacing, padding, setDefault, defaultLabel, remove);

        name.TextChanged += (_, _) =>
        {
            style.Name = (name.Text ?? string.Empty).Trim();
            heading.Text = DisplayName(style.Name);
            if (ReferenceEquals(style, _defaultStyle))
            {
                _config.SelectedTextStyle = style.Name;
            }

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
            {
                style.FontSize = (double)value;
            }

            Revalidate();
        };
        lineSpacing.ValueChanged += (_, _) =>
        {
            if (lineSpacing.Value is { } value)
            {
                style.LineSpacing = (double)value;
            }

            Revalidate();
        };
        padding.ValueChanged += (_, _) =>
        {
            if (padding.Value is { } value)
            {
                style.Padding = (double)value;
            }

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

    private void MakeDefault(EditorTextStyle style)
    {
        _defaultStyle = style;
        _config.SelectedTextStyle = style.Name;
        UpdateDefaultIndicators();
        Revalidate();
    }

    private void AddStyle()
    {
        var template = _defaultStyle ?? _config.TextStyles.FirstOrDefault();
        var style = template?.Copy() ?? new EditorTextStyle();
        style.Name = UniqueName(template is null ? "New style" : template.Name + " copy");
        _config.TextStyles.Add(style);
        // Adding a preset does not change which one is the default.
        RebuildStyleCards(_defaultStyle);
        Revalidate();
    }

    private void RemoveStyle(EditorTextStyle style)
    {
        // The default preset is undeletable, which guarantees at least one preset always remains.
        if (ReferenceEquals(style, _defaultStyle) || _config.TextStyles.Count <= 1)
        {
            return;
        }

        _config.TextStyles.Remove(style);
        RebuildStyleCards(_defaultStyle);
        Revalidate();
    }

    private void RebuildStyleCards(EditorTextStyle? @default)
    {
        _defaultStyle = @default ?? _config.TextStyles.FirstOrDefault();
        if (_defaultStyle is not null)
        {
            _config.SelectedTextStyle = _defaultStyle.Name;
        }

        _styleEditors.Clear();
        _styleItems.Children.Clear();
        foreach (var style in _config.TextStyles)
        {
            _styleItems.Children.Add(BuildStyleCard(style));
        }

        UpdateDefaultIndicators();
    }

    /// <summary>Reflects the current default across every card: the default pill / Set-as-default
    /// button, and which cards may be removed (never the default, never the last remaining one).</summary>
    private void UpdateDefaultIndicators()
    {
        foreach (var (style, editor) in _styleEditors)
        {
            var isDefault = ReferenceEquals(style, _defaultStyle);
            editor.SetDefault.IsVisible = !isDefault;
            editor.DefaultLabel.IsVisible = isDefault;
            editor.Remove.IsEnabled = !isDefault && _config.TextStyles.Count > 1;
        }
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var counter = 2;
        while (_config.TextStyles.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName} {counter++}";
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

        if (_config.TextStyles.Count == 0 || _styleEditors.Count != _config.TextStyles.Count)
        {
            return false;
        }

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

        return _defaultStyle is not null;
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
        TextBox Name,
        TextBox FontFamily,
        NumericUpDown FontSize,
        NumericUpDown LineSpacing,
        NumericUpDown Padding,
        Button SetDefault,
        Border DefaultLabel,
        Button Remove);
}
