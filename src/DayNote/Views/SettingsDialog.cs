using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DayNote.Controls;
using DayNote.Core.Configuration;
using DayNote.Core.Time;

namespace DayNote.Views;

/// <summary>
/// The custom settings dialog. Edits a working copy of <see cref="AppConfig"/> in place: controls
/// write straight to that copy, so the caller applies the edits by keeping the copy on Save and
/// discards it on Cancel. Every text-style preset is an independent editable card; exactly one is the
/// default and the default cannot be removed, which guarantees at least one preset always remains.
/// </summary>
public sealed class SettingsDialog : DialogBase
{
    private readonly AppConfig _config;
    private readonly AppConfig _original;
    private readonly StackPanel _styleItems = new() { Spacing = 8 };
    private readonly Dictionary<EditorTextStyle, StyleEditorControls> _styleEditors = [];
    private readonly TextBox _uiFont;
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
        styleActions.Children.Add(Utility("Reset to latest defaults", ResetStyles));
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

        _uiFont = new ComposingTextBox { Text = config.UiFontFamily, PlaceholderText = AppConfig.DefaultUiFontFamily };
        _autosave = Numeric((decimal)SettingsValidator.MinAutosaveSeconds, (decimal)SettingsValidator.MaxAutosaveSeconds, 0.25m);
        _autosave.Value = (decimal)config.AutosaveDelaySeconds;
        _timeZone = new ComposingTextBox { Text = config.DisplayTimeZone };

        var panel = new StackPanel { Spacing = 8, Width = 540 };
        panel.Children.Add(styleHeader);
        panel.Children.Add(styleList);
        // Discardable-draft hint for "Reset to latest defaults" (config-seeding-conventions): the reset
        // mutates only this working copy, so close-without-save is the safety net — a hint, not a confirm.
        panel.Children.Add(new TextBlock
        {
            Text = "Replaces your text styles with the latest built-in presets. " +
                   "Applies on Save; Cancel to keep your current styles.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = PaletteBrush.Resolve("TextSecondaryBrush"),
        });
        // The UI (chrome) font sits with the appearance settings, just below the editor text styles;
        // it governs the whole app's chrome, while the styles above govern the note body.
        panel.Children.Add(Label("UI font (comma-separated; first installed is used; blank = Inter)"));
        panel.Children.Add(_uiFont);
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
        _uiFont.TextChanged += (_, _) =>
        {
            // Free text; blank is allowed and resolves to the bundled default at apply time.
            _config.UiFontFamily = (_uiFont.Text ?? string.Empty).Trim();
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

        var name = new ComposingTextBox { Text = style.Name, PlaceholderText = "Preset name" };
        var fontFamily = new ComposingTextBox { Text = style.FontFamily, PlaceholderText = "Font family (e.g. Menlo)" };
        var fontSize = Numeric((decimal)SettingsValidator.MinFontSize, (decimal)SettingsValidator.MaxFontSize, 1);
        fontSize.Value = (decimal)style.FontSize;
        var lineSpacing = Numeric((decimal)SettingsValidator.MinLineSpacing, (decimal)SettingsValidator.MaxLineSpacing, 0.1m);
        lineSpacing.Value = (decimal)style.LineSpacing;
        var padding = Numeric((decimal)SettingsValidator.MinPadding, (decimal)SettingsValidator.MaxPadding, 1);
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
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = PaletteBrush.Resolve("AccentForegroundBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
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

    private void ResetStyles()
    {
        // Wholesale restore per the config-seeding-conventions: replace the entire set AND the
        // selection with the current built-in defaults, drawn from the same source that seeds a
        // first run — so a later version's improved presets reach an existing user. This edits only
        // the working copy; Save commits it, Cancel discards it (hence the hint, not a blocking confirm).
        _config.TextStyles = AppConfig.DefaultTextStyles();
        _config.SelectedTextStyle = AppConfig.DefaultSelectedTextStyle;
        RebuildStyleCards(_config.ResolveSelectedStyle());
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

    private string UniqueName(string baseName) =>
        SettingsValidator.UniqueName(baseName, _config.TextStyles.Select(s => s.Name));

    private void Revalidate() => _saveButton.IsEnabled = IsValid() && SettingsValidator.IsDirty(_config, _original);

    private bool IsValid()
    {
        // UI invariant: every style must have a matching editor before we can read its
        // controls; a mismatch (or no styles) is not a savable state.
        if (_config.TextStyles.Count == 0 || _styleEditors.Count != _config.TextStyles.Count)
        {
            return false;
        }

        var styles = _config.TextStyles.Select(style =>
        {
            var controls = _styleEditors[style];
            return new TextStyleDraft(
                controls.Name.Text ?? string.Empty,
                controls.FontFamily.Text ?? string.Empty,
                (double)(controls.FontSize.Value ?? 0),
                (double)(controls.LineSpacing.Value ?? 0),
                (double)(controls.Padding.Value ?? 0));
        }).ToList();

        var draft = new SettingsDraft(
            _timeZone.Text ?? string.Empty,
            (double)(_autosave.Value ?? 0),
            styles,
            _defaultStyle is not null);

        return SettingsValidator.IsValid(draft);
    }

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
