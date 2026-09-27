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
using DayNote.Core.Time;
using DayNote.I18n;

namespace DayNote.Views;

/// <summary>
/// The custom settings dialog. It edits a working copy of <see cref="AppConfig"/> in place: the caller
/// keeps the copy on Save and discards it on Cancel. Text-style presets are one list, whose order is
/// durable and is the order Cmd/Ctrl+J cycles through, and one editor for the selected preset. A preset
/// goes by its font family; one preset is the default, marked in the list, and cannot be removed.
/// </summary>
public sealed class SettingsDialog : DialogBase
{
    private static readonly (ThemePreference Value, string LabelKey)[] ThemeChoices =
    [
        (ThemePreference.System, "settings.themeSystem"),
        (ThemePreference.Light, "settings.themeLight"),
        (ThemePreference.Dark, "settings.themeDark"),
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
    private readonly ComboBox _language;
    private readonly IReadOnlyList<RadioButton> _themeButtons;
    private readonly TextBox _uiFont;
    private readonly NumericUpDown _autosave;
    private readonly ComboBox _timeZone;
    private readonly Button _saveButton;
    private readonly TextBlock _saveError;
    private readonly Func<AppConfig, bool> _trySave;
    private readonly Func<string, Task<bool>> _askBeforeRemoving;
    private bool _loadingStyleEditor;

    /// <param name="askBeforeRemoving">
    /// Asks whether the named style may go. The default asks in a dialog stacked over this one, which
    /// is what the trigger of a destructive path owes; a caller passes its own to answer without one.
    /// </param>
    public SettingsDialog(
        AppConfig config,
        Func<AppConfig, bool> trySave,
        Func<string, Task<bool>>? askBeforeRemoving = null)
    {
        _config = config;
        _trySave = trySave;
        _askBeforeRemoving = askBeforeRemoving ?? AskBeforeRemovingAsync;
        Localized.SetTitle(this, "settings.title");
        Width = 660;

        // The preset list. Add belongs to the list; the selected preset's own actions sit with its
        // editor. Drag or Cmd/Ctrl+Shift+Up/Down reorders it, which is also the cycling order.
        _styleList = new ListBox
        {
            Name = "TextStylesList",
            ItemsSource = _styleRows,
            ItemTemplate = new FuncDataTemplate<StyleRow>((_, _) => StyleRowView()),
        };
        _styleList.BorderThickness = new Thickness(1);
        _styleList.CornerRadius = new CornerRadius(6);
        _styleList.Themed(ListBox.BorderBrushProperty, "BorderBrush");
        Localized.SetAutomationName(_styleList, "settings.textStyles");
        DragDrop.SetAllowDrop(_styleList, true);
        _styleList.SelectionChanged += (_, _) => LoadSelectedStyle();
        _ = new ListReorder<StyleRow>(_styleList, canReorder: null, MoveStyle, Revalidate, () => _styleRows.ToArray(), RestoreStyles);

        var addStyle = Utility("common.add", AddStyle, "AddTextStyleButton");
        addStyle.HorizontalAlignment = HorizontalAlignment.Right;
        var listHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var listLabel = Label("settings.textStyles");
        listLabel.VerticalAlignment = VerticalAlignment.Center;
        listHeader.Children.Add(listLabel);
        Grid.SetColumn(addStyle, 1);
        listHeader.Children.Add(addStyle);

        _styleFontFamily = new ComposingTextBox { Name = "TextStyleFontFamily" };
        Localized.SetPlaceholderText(_styleFontFamily, "settings.fontFamilyPlaceholder");
        _styleFontSize = Numeric((decimal)SettingsValidator.MinFontSize, (decimal)SettingsValidator.MaxFontSize, 1);
        _styleLineSpacing = Numeric((decimal)SettingsValidator.MinLineSpacing, (decimal)SettingsValidator.MaxLineSpacing, 0.1m);
        _stylePadding = Numeric((decimal)SettingsValidator.MinPadding, (decimal)SettingsValidator.MaxPadding, 1);
        _styleBold = new CheckBox();
        Localized.SetContent(_styleBold, "settings.bold");
        _styleItalic = new CheckBox();
        Localized.SetContent(_styleItalic, "settings.italic");
        _setDefault = Utility("settings.setDefault", MakeSelectedDefault, "SetDefaultTextStyleButton");
        _removeStyle = new Button { Name = "RemoveTextStyleButton" };
        Localized.SetContent(_removeStyle, "common.remove");
        _removeStyle.Classes.Add("danger");
        _removeStyle.Click += (_, _) => RemoveSelectedStyle();

        var decorations = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };
        decorations.Children.Add(_styleBold);
        decorations.Children.Add(_styleItalic);

        var styleActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        styleActions.Children.Add(_setDefault);
        styleActions.Children.Add(_removeStyle);

        // The preset's marks and the preset's own actions share one line: the checkboxes read left,
        // the actions sit at the trailing edge where every other action row in this app puts them.
        // Where a language's labels are too long for one line, the actions take a line of their own.
        var decorationRow = new LeadingTrailingRow(decorations, styleActions) { Spacing = 16, LineSpacing = 10 };

        var editor = new StackPanel { Spacing = 10 };
        editor.Children.Add(Field("settings.fontFamily", _styleFontFamily));
        editor.Children.Add(Row(
            ("*", Field("settings.fontSize", _styleFontSize)),
            ("*", Field("settings.lineSpacing", _styleLineSpacing)),
            ("*", Field("settings.padding", _stylePadding))));
        editor.Children.Add(decorationRow);

        // The list's header and the editor's first field start on one line, so the right half has no
        // empty band over it, and the list's own bounds are drawn rather than left to its rows. The
        // editor spans both rows, so the second row takes the star: with two Auto rows the grid pads
        // the header's row to share the editor's height, which pushes "Text styles" down the surface
        // and leaves a band of nothing under it. The star row instead puts the header at the top and
        // closes the list level with the editor's last line, so the settings below the surface start
        // where the editor ends rather than under a column of nothing.
        var styleSurface = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("200,*"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            ColumnSpacing = 16,
            RowSpacing = 6,
        };
        styleSurface.Children.Add(listHeader);
        Grid.SetRow(_styleList, 1);
        styleSurface.Children.Add(_styleList);
        Grid.SetColumn(editor, 1);
        Grid.SetRowSpan(editor, 2);
        styleSurface.Children.Add(editor);

        // System first, then each language by its own name, so a reader finds theirs whatever language
        // is showing. Applied on Save like every other field here.
        var languages = LanguageOption.All();
        _language = new ComboBox
        {
            Name = "LanguageBox",
            ItemsSource = languages,
            SelectedItem = LanguageOption.For(config.Language, languages),
            DisplayMemberBinding = new Binding(nameof(LanguageOption.Name)),
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Localized.SetAutomationName(_language, "settings.language");

        _themeButtons = ThemeChoices
            .Select(choice =>
            {
                var button = new RadioButton { IsChecked = choice.Value == config.Theme };
                Localized.SetContent(button, choice.LabelKey);
                return button;
            })
            .ToList();
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        foreach (var button in _themeButtons)
        {
            themeRow.Children.Add(button);
        }

        Localized.SetAutomationName(themeRow, "settings.theme");
        var themeHint = Hint("settings.themeHint");

        _uiFont = new ComposingTextBox { Text = config.UiFontFamily, PlaceholderText = AppConfig.DefaultUiFontFamily };
        _autosave = Numeric((decimal)SettingsValidator.MinAutosaveSeconds, (decimal)SettingsValidator.MaxAutosaveSeconds, 0.25m);
        _autosave.Value = (decimal)config.AutosaveDelaySeconds;
        // System first, then every zone the platform knows by its IANA id: chosen, never typed.
        var zones = TimeZoneOption.All(config.TimeZone);
        var zone = TimeZoneOption.For(config.TimeZone, zones);
        // A hand-edited id no zone answers to already displays as System; the draft says so too, so
        // it is not an invalid value holding Save disabled behind a list that shows System.
        config.TimeZone = zone.Value;
        _timeZone = new ComboBox
        {
            Name = "TimeZoneBox",
            ItemsSource = zones,
            SelectedItem = zone,
            DisplayMemberBinding = new Binding(nameof(TimeZoneOption.Name)),
            MinWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Localized.SetAutomationName(_timeZone, "settings.timeZone");

        var panel = new StackPanel { Spacing = 8, Width = 600 };
        panel.Children.Add(Label("settings.language"));
        panel.Children.Add(_language);
        panel.Children.Add(styleSurface);
        panel.Children.Add(Label("settings.theme"));
        panel.Children.Add(themeRow);
        panel.Children.Add(themeHint);
        panel.Children.Add(Label("settings.uiFont"));
        panel.Children.Add(_uiFont);
        panel.Children.Add(Hint("settings.uiFontHint"));
        panel.Children.Add(Label("settings.autosave"));
        panel.Children.Add(_autosave);
        panel.Children.Add(Label("settings.timeZone"));
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
        var buttons = SetButtons([new DialogButton("common.cancel", "cancel"), new DialogButton("common.save", "ok", DialogButtonKind.Primary)]);
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
        _language.SelectionChanged += (_, _) =>
        {
            if (_language.SelectedItem is LanguageOption language)
            {
                _config.Language = language.Value;
            }

            Revalidate();
        };
        _timeZone.SelectionChanged += (_, _) =>
        {
            if (_timeZone.SelectedItem is TimeZoneOption zone)
            {
                _config.TimeZone = zone.Value;
            }

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

        _saveError.Text = Localizer.T("failure.settingsSave");
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

    private async void RemoveSelectedStyle()
    {
        if (SelectedStyleRow() is not { } row || row.Style.IsDefault || _styleRows.Count <= 1)
        {
            return;
        }

        if (!await _askBeforeRemoving(row.Label))
        {
            return;
        }

        // The list may have moved while the question stood.
        if (!_styleRows.Contains(row) || row.Style.IsDefault || _styleRows.Count <= 1)
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

    private async Task<bool> AskBeforeRemovingAsync(string label)
    {
        var dialog = new MessageDialog(
            Message.Of("settings.removeStyleTitle"),
            Message.Of("settings.removeStyleMessage", ("style", label)),
            [
                new DialogButton("common.cancel", "cancel"),
                new DialogButton("common.remove", "confirm", DialogButtonKind.Destructive),
            ]);
        await dialog.ShowBoundedAsync(this);
        return dialog.ResultTag == "confirm";
    }

    private void RefreshStyleRows()
    {
        var labels = TextStyleLabels.For(_config.TextStyles, Localizer.T("settings.noFontFamily"), Localizer.Current.Culture);
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
            _config.TimeZone,
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
        var badgeText = new TextBlock { FontSize = 11, FontWeight = FontWeight.SemiBold }
            .Themed(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Localized.SetText(badgeText, "settings.defaultBadge");
        var badge = new Border { Child = badgeText };
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

    private static Button Utility(string labelKey, Action onClick, string? name = null)
    {
        var button = new Button { Name = name };
        Localized.SetContent(button, labelKey);
        button.Classes.Add("utility");
        button.Click += (_, _) => onClick();
        return button;
    }

    // A field's label wraps rather than running past its third of the editor in a longer language.
    private static StackPanel Field(string labelKey, Control control)
    {
        var field = new StackPanel { Spacing = 4 };
        var label = new TextBlock { FontWeight = FontWeight.SemiBold, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        Localized.SetText(label, labelKey);
        field.Children.Add(label);
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

    // Section labels and hints wrap inside the dialog's fixed width in every language.
    private static TextBlock Label(string key)
    {
        var label = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        Localized.SetText(label, key);
        return label;
    }

    private static TextBlock Hint(string key)
    {
        var hint = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap }
            .Themed(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Localized.SetText(hint, key);
        return hint;
    }

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

/// <summary>
/// A line in the Settings time-zone list: the value that is saved, and the words shown for it. System
/// names the zone it follows now, so the reader sees which zone that is on this computer; the zones
/// themselves go by their IANA ids, which read the same in every language.
/// </summary>
public sealed record TimeZoneOption(string Value, string Name)
{
    /// <summary>System first, then each zone by its IANA id.</summary>
    internal static IReadOnlyList<TimeZoneOption> All(string? saved) =>
    [
        new(DayNoteTime.SystemZone, Localizer.T("settings.timeZoneSystem", ("zone", DayNoteTime.SystemZoneId()))),
        .. DayNoteTime.ZoneIds(saved).Select(id => new TimeZoneOption(id, id)),
    ];

    /// <summary>The line for a saved setting, falling back to System for one no zone answers to.</summary>
    internal static TimeZoneOption For(string? saved, IReadOnlyList<TimeZoneOption> options) =>
        DayNoteTime.IsSystem(saved)
            ? options[0]
            : options.Skip(1).FirstOrDefault(option => option.Value == saved!.Trim()) ?? options[0];
}
