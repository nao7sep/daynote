using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DayNote.Controls;
using DayNote.Core.Configuration;

namespace DayNote.Views;

/// <summary>
/// The custom settings dialog. It edits a working copy of <see cref="AppConfig"/> in place: the caller
/// keeps the copy on Save and discards it on Cancel. Text-style presets are one list, whose order is
/// durable and is the order Cmd/Ctrl+J cycles through, and one editor for the selected preset. A preset
/// goes by its font family; one preset is the default, marked in the list, and cannot be removed.
/// </summary>
public sealed class SettingsDialog : DialogBase
{
    private static readonly (ThemePreference Value, string Label)[] ThemeChoices =
    [
        (ThemePreference.System, "System"),
        (ThemePreference.Light, "Light"),
        (ThemePreference.Dark, "Dark"),
    ];

    private readonly AppConfig _config;
    private readonly AppConfig _original;
    private readonly ObservableCollection<StyleRow> _styleRows = [];
    private readonly ListBox _styleList;
    private readonly ComposingTextBox _styleFontFamily;
    private readonly NumericUpDown _styleFontSize;
    private readonly NumericUpDown _styleLineSpacing;
    private readonly NumericUpDown _stylePadding;
    private readonly CheckBox _styleBold;
    private readonly CheckBox _styleItalic;
    private readonly Button _setDefault;
    private readonly Button _removeStyle;
    private readonly IReadOnlyList<RadioButton> _themeButtons;
    private readonly TextBox _uiFont;
    private readonly NumericUpDown _autosave;
    private readonly TextBox _timeZone;
    private readonly Button _saveButton;
    private readonly TextBlock _saveError;
    private readonly Func<AppConfig, bool> _trySave;
    private bool _loadingStyleEditor;

    public SettingsDialog(AppConfig config, Func<AppConfig, bool> trySave)
    {
        _config = config;
        _trySave = trySave;
        Title = "Settings";
        Width = 600;

        // The preset list. Add belongs to the list; the selected preset's own actions sit with its
        // editor. Drag or Cmd/Ctrl+Shift+Up/Down reorders it, which is also the cycling order.
        _styleList = new ListBox
        {
            Name = "TextStylesList",
            ItemsSource = _styleRows,
            ItemTemplate = new FuncDataTemplate<StyleRow>((_, _) => StyleRowView()),
        };
        AutomationProperties.SetName(_styleList, "Text styles");
        DragDrop.SetAllowDrop(_styleList, true);
        _styleList.SelectionChanged += (_, _) => LoadSelectedStyle();
        _ = new ListReorder<StyleRow>(_styleList, canReorder: null, MoveStyle, Revalidate, () => _styleRows.ToArray(), RestoreStyles);

        var addStyle = Utility("Add", AddStyle, "AddTextStyleButton");
        addStyle.HorizontalAlignment = HorizontalAlignment.Left;
        addStyle.Margin = new Thickness(0, 8, 0, 0);
        var listColumn = new DockPanel();
        DockPanel.SetDock(addStyle, Dock.Bottom);
        listColumn.Children.Add(addStyle);
        listColumn.Children.Add(_styleList);

        _styleFontFamily = new ComposingTextBox
        {
            Name = "TextStyleFontFamily",
            PlaceholderText = "Font family (e.g. Menlo)",
        };
        _styleFontSize = Numeric((decimal)SettingsValidator.MinFontSize, (decimal)SettingsValidator.MaxFontSize, 1);
        _styleLineSpacing = Numeric((decimal)SettingsValidator.MinLineSpacing, (decimal)SettingsValidator.MaxLineSpacing, 0.1m);
        _stylePadding = Numeric((decimal)SettingsValidator.MinPadding, (decimal)SettingsValidator.MaxPadding, 1);
        _styleBold = new CheckBox { Content = "Bold" };
        _styleItalic = new CheckBox { Content = "Italic" };
        _setDefault = Utility("Set as default", MakeSelectedDefault, "SetDefaultTextStyleButton");
        _removeStyle = Utility("Remove", RemoveSelectedStyle, "RemoveTextStyleButton");

        var decorations = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        decorations.Children.Add(_styleBold);
        decorations.Children.Add(_styleItalic);

        var styleActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        styleActions.Children.Add(_setDefault);
        styleActions.Children.Add(_removeStyle);

        var editor = new StackPanel { Spacing = 10 };
        editor.Children.Add(Field("Font family", _styleFontFamily));
        editor.Children.Add(Row(
            ("*", Field("Font size", _styleFontSize)),
            ("*", Field("Line spacing", _styleLineSpacing)),
            ("*", Field("Padding", _stylePadding))));
        editor.Children.Add(decorations);
        editor.Children.Add(styleActions);

        var styleSurface = new Grid
        {
            Height = 240,
            ColumnDefinitions = new ColumnDefinitions("180,*"),
            ColumnSpacing = 16,
        };
        styleSurface.Children.Add(listColumn);
        Grid.SetColumn(editor, 1);
        styleSurface.Children.Add(editor);

        _themeButtons = ThemeChoices
            .Select(choice => new RadioButton { Content = choice.Label, IsChecked = choice.Value == config.Theme })
            .ToList();
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        foreach (var button in _themeButtons)
        {
            themeRow.Children.Add(button);
        }

        AutomationProperties.SetName(themeRow, "Theme");
        var themeHint = new TextBlock { Text = "System follows the OS appearance.", FontSize = 12 }
            .Themed(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        _uiFont = new ComposingTextBox { Text = config.UiFontFamily, PlaceholderText = AppConfig.DefaultUiFontFamily };
        _autosave = Numeric((decimal)SettingsValidator.MinAutosaveSeconds, (decimal)SettingsValidator.MaxAutosaveSeconds, 0.25m);
        _autosave.Value = (decimal)config.AutosaveDelaySeconds;
        _timeZone = new ComposingTextBox { Text = config.DisplayTimeZone };

        var panel = new StackPanel { Spacing = 8, Width = 540 };
        panel.Children.Add(Label("Text styles"));
        panel.Children.Add(styleSurface);
        panel.Children.Add(Label("Theme"));
        panel.Children.Add(themeRow);
        panel.Children.Add(themeHint);
        panel.Children.Add(Label("UI font (comma-separated; first installed is used; blank = Inter)"));
        panel.Children.Add(_uiFont);
        panel.Children.Add(Label("Autosave delay (seconds)"));
        panel.Children.Add(_autosave);
        panel.Children.Add(Label("Display time zone (IANA id, e.g. Asia/Tokyo)"));
        panel.Children.Add(_timeZone);
        _saveError = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        }.Themed(TextBlock.ForegroundProperty, "DangerTextBrush");
        AutomationProperties.SetLiveSetting(_saveError, AutomationLiveSetting.Assertive);
        panel.Children.Add(_saveError);

        SetContent(panel);
        var buttons = SetButtons([new DialogButton("Cancel", "cancel"), new DialogButton("Save", "ok", DialogButtonKind.Primary)]);
        _saveButton = buttons["ok"];

        BuildStyleList();
        WireStyleEditor();
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
        foreach (var (button, choice) in _themeButtons.Zip(ThemeChoices))
        {
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true)
                {
                    _config.Theme = choice.Value;
                }

                Revalidate();
            };
        }

        _uiFont.TextChanged += (_, _) =>
        {
            _config.UiFontFamily = (_uiFont.Text ?? string.Empty).Trim();
            Revalidate();
        };

        Revalidate();
        SetInitialFocus(_styleList);
    }

    public bool Applied => ResultTag == "ok";

    protected override bool TryCommit(string tag)
    {
        if (tag != "ok")
        {
            return true;
        }

        _saveError.IsVisible = false;
        _saveError.Text = string.Empty;
        if (_trySave(_config))
        {
            return true;
        }

        _saveError.Text = "Settings could not be saved. Your changes are still here; try again.";
        _saveError.IsVisible = true;
        return false;
    }

    private void BuildStyleList()
    {
        foreach (var style in _config.TextStyles)
        {
            _styleRows.Add(new StyleRow(style));
        }

        RefreshStyleRows();
        _styleList.SelectedItem = _styleRows.FirstOrDefault(row => row.Style.IsDefault) ?? _styleRows.FirstOrDefault();
    }

    private void WireStyleEditor()
    {
        _styleFontFamily.TextChanged += (_, _) => UpdateSelectedStyle(style =>
            style.FontFamily = (_styleFontFamily.Text ?? string.Empty).Trim());
        WireNumber(_styleFontSize, (style, value) => style.FontSize = value, style => style.FontSize);
        WireNumber(_styleLineSpacing, (style, value) => style.LineSpacing = value, style => style.LineSpacing);
        WireNumber(_stylePadding, (style, value) => style.Padding = value, style => style.Padding);
        _styleBold.IsCheckedChanged += (_, _) => UpdateSelectedStyle(style => style.Bold = _styleBold.IsChecked == true);
        _styleItalic.IsCheckedChanged += (_, _) => UpdateSelectedStyle(style => style.Italic = _styleItalic.IsChecked == true);
    }

    // A cleared number box keeps the preset's last value and shows it again when focus leaves, so a
    // number is never stored that the field does not show.
    private void WireNumber(NumericUpDown box, Action<EditorTextStyle, double> set, Func<EditorTextStyle, double> get)
    {
        box.ValueChanged += (_, _) =>
        {
            if (box.Value is { } value)
            {
                UpdateSelectedStyle(style => set(style, (double)value));
            }
        };
        box.LostFocus += (_, _) =>
        {
            if (box.Value is null && SelectedStyleRow() is { } row)
            {
                _loadingStyleEditor = true;
                box.Value = (decimal)get(row.Style);
                _loadingStyleEditor = false;
            }
        };
    }

    private void UpdateSelectedStyle(Action<EditorTextStyle> update)
    {
        if (_loadingStyleEditor || SelectedStyleRow() is not { } row)
        {
            return;
        }

        update(row.Style);
        RefreshStyleRows();
        Revalidate();
    }

    private void LoadSelectedStyle()
    {
        _loadingStyleEditor = true;
        if (SelectedStyleRow() is { } row)
        {
            var style = row.Style;
            _styleFontFamily.Text = style.FontFamily;
            _styleFontSize.Value = (decimal)style.FontSize;
            _styleLineSpacing.Value = (decimal)style.LineSpacing;
            _stylePadding.Value = (decimal)style.Padding;
            _styleBold.IsChecked = style.Bold;
            _styleItalic.IsChecked = style.Italic;
        }

        _loadingStyleEditor = false;
        UpdateStyleActions();
    }

    private StyleRow? SelectedStyleRow() => _styleList.SelectedItem as StyleRow;

    /// <summary>
    /// Appends a preset with the app's built-in values, selects and reveals it, and puts the caret in
    /// its font family, the field that names it.
    /// </summary>
    private void AddStyle()
    {
        var row = new StyleRow(new EditorTextStyle());
        _config.TextStyles.Add(row.Style);
        _styleRows.Add(row);
        RefreshStyleRows();
        _styleList.SelectedItem = row;
        _styleList.ScrollIntoView(row);
        Revalidate();

        Dispatcher.UIThread.Post(() =>
        {
            _styleList.ScrollIntoView(row);
            _styleFontFamily.Focus();
            _styleFontFamily.SelectAll();
        });
    }

    private bool MoveStyle(StyleRow row, StyleRow target)
    {
        var from = _styleRows.IndexOf(row);
        var to = _styleRows.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return false;
        }

        _styleRows.Move(from, to);
        _config.TextStyles.RemoveAt(from);
        _config.TextStyles.Insert(to, row.Style);
        return true;
    }

    private bool RestoreStyles(IReadOnlyList<StyleRow> order)
    {
        if (order.Count != _styleRows.Count || order.Any(row => !_styleRows.Contains(row)))
        {
            return false;
        }

        for (var index = 0; index < order.Count; index++)
        {
            MoveStyle(order[index], _styleRows[index]);
        }

        return true;
    }

    private void MakeSelectedDefault()
    {
        if (SelectedStyleRow() is not { } selected)
        {
            return;
        }

        foreach (var row in _styleRows)
        {
            row.Style.IsDefault = ReferenceEquals(row, selected);
        }

        RefreshStyleRows();
        UpdateStyleActions();
        Revalidate();
    }

    private void RemoveSelectedStyle()
    {
        if (SelectedStyleRow() is not { } row || row.Style.IsDefault || _styleRows.Count <= 1)
        {
            return;
        }

        var index = _styleRows.IndexOf(row);
        _config.TextStyles.Remove(row.Style);
        _styleRows.Remove(row);
        RefreshStyleRows();
        _styleList.SelectedItem = _styleRows[Math.Min(index, _styleRows.Count - 1)];
        Revalidate();
    }

    private void RefreshStyleRows()
    {
        var labels = TextStyleLabels.For(_config.TextStyles, UiFont.EditorFamilyName);
        for (var index = 0; index < _styleRows.Count; index++)
        {
            _styleRows[index].Refresh(labels[index]);
        }
    }

    private void UpdateStyleActions()
    {
        var row = SelectedStyleRow();
        _setDefault.IsEnabled = row is not null && !row.Style.IsDefault;
        _removeStyle.IsEnabled = row is not null && !row.Style.IsDefault && _styleRows.Count > 1;
    }

    private void Revalidate() => _saveButton.IsEnabled = IsValid() && SettingsValidator.IsDirty(_config, _original);

    private bool IsValid()
    {
        var styles = _config.TextStyles.Select(style => new TextStyleDraft(
            style.FontFamily,
            style.FontSize,
            style.LineSpacing,
            style.Padding)).ToList();
        var draft = new SettingsDraft(
            _timeZone.Text ?? string.Empty,
            (double)(_autosave.Value ?? 0),
            styles,
            _config.TextStyles.Count(style => style.IsDefault) == 1);
        return SettingsValidator.IsValid(draft);
    }

    // One row: the preset's label, and a "Default" badge on the default preset so it shows without
    // selecting each row in turn.
    private static Control StyleRowView()
    {
        var label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        label.Bind(TextBlock.TextProperty, new Binding(nameof(StyleRow.Label)));
        var badge = new Border
        {
            Child = new TextBlock { Text = "Default", FontSize = 11, FontWeight = FontWeight.SemiBold }
                .Themed(TextBlock.ForegroundProperty, "TextSecondaryBrush"),
        };
        badge.Classes.Add("badge");
        badge.Bind(IsVisibleProperty, new Binding(nameof(StyleRow.IsDefault)));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(label);
        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);
        return row;
    }

    // Minus and plus, each 1.5 units thick in a 10x10 box, so the two glyphs carry one weight.
    private const string MinusGlyph = "M0,4.25 H10 V5.75 H0 Z";
    private const string PlusGlyph = "M4.25,0 H5.75 V4.25 H10 V5.75 H5.75 V10 H4.25 V5.75 H0 V4.25 H4.25 Z";

    private static NumericUpDown Numeric(decimal min, decimal max, decimal increment)
    {
        var numeric = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = increment,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        numeric.TemplateApplied += (_, applied) =>
        {
            if (applied.NameScope.Find<ButtonSpinner>("PART_Spinner") is { } spinner)
            {
                spinner.TemplateApplied += (_, spinnerApplied) => UseMinusAndPlus(spinnerApplied.NameScope);
            }
        };

        return numeric;
    }

    /// <summary>
    /// Makes the spinner a minus and a plus, minus first. The theme pairs an up and a down chevron
    /// side by side, which no ordinary numeric control does: a chevron pair means one step each way
    /// when it is stacked, and side by side that pair is minus and plus. The theme sets each glyph
    /// inside its own template, where a style cannot reach it, so the buttons are dressed here.
    /// </summary>
    private static void UseMinusAndPlus(INameScope scope)
    {
        var decrease = scope.Find<RepeatButton>("PART_DecreaseButton");
        var increase = scope.Find<RepeatButton>("PART_IncreaseButton");
        if (decrease is null || increase is null)
        {
            return;
        }

        decrease.Content = new PathIcon { Width = 10, Height = 10, Data = Geometry.Parse(MinusGlyph) };
        increase.Content = new PathIcon { Width = 10, Height = 10, Data = Geometry.Parse(PlusGlyph) };
        if (scope.Find<StackPanel>("PART_SpinnerPanel") is { } panel
            && panel.Children.IndexOf(decrease) > panel.Children.IndexOf(increase))
        {
            panel.Children.Move(panel.Children.IndexOf(decrease), panel.Children.IndexOf(increase));
        }
    }

    private static Button Utility(string text, Action onClick, string? name = null)
    {
        var button = new Button { Content = text, Name = name };
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
            ColumnDefinitions = new ColumnDefinitions(string.Join(",", cells.Select(cell => cell.Width))),
            ColumnSpacing = 8,
        };
        for (var index = 0; index < cells.Length; index++)
        {
            Grid.SetColumn(cells[index].Child, index);
            grid.Children.Add(cells[index].Child);
        }

        return grid;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };

    private sealed class StyleRow(EditorTextStyle style) : INotifyPropertyChanged
    {
        public EditorTextStyle Style { get; } = style;
        public string Label { get; private set; } = string.Empty;
        public bool IsDefault => Style.IsDefault;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Refresh(string label)
        {
            Label = label;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDefault)));
        }
    }
}
