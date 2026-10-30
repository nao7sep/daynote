using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DayNote.Controls;
using DayNote.Core.Configuration;
using DayNote.I18n;

namespace DayNote.Views;

/// <summary>
/// A field where the user types a number (number-field-conventions): a text box that keeps what was typed,
/// a minus and a plus that step from the last valid value, and, while the text is not a number the range
/// accepts, a message under the field saying what it accepts. Only a valid value is committed. The
/// toolkit's NumericUpDown is not used because its text cannot stay invalid: it restores or clamps it.
/// </summary>
public sealed class NumberField : Grid
{
    // Minus and plus, each 1.5 units thick in a 10x10 box, so the two glyphs carry one weight.
    private const string MinusGlyph = "M0,4.25 H10 V5.75 H0 Z";
    private const string PlusGlyph = "M4.25,0 H5.75 V4.25 H10 V5.75 H5.75 V10 H4.25 V5.75 H0 V4.25 H4.25 Z";

    private readonly NumberRange _range;
    private readonly double _step;
    private bool _showing;

    public NumberField(NumberRange range, double step, string? name = null)
    {
        _range = range;
        _step = step;
        Name = name;
        ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
        RowDefinitions = new RowDefinitions("Auto,Auto");
        ColumnSpacing = 4;

        Input = new ComposingTextBox { Name = name is null ? null : name + "Text" };
        Decrease = StepButton(MinusGlyph, "numberField.decrease", -1);
        Increase = StepButton(PlusGlyph, "numberField.increase", 1);
        Error = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0),
        }.Themed(TextBlock.ForegroundProperty, "DangerTextBrush");
        AutomationProperties.SetLiveSetting(Error, AutomationLiveSetting.Assertive);

        Children.Add(Input);
        SetColumn(Decrease, 1);
        Children.Add(Decrease);
        SetColumn(Increase, 2);
        Children.Add(Increase);
        SetRow(Error, 1);
        SetColumnSpan(Error, 3);
        Children.Add(Error);

        Input.TextChanged += (_, _) => Read();
        UpdateButtons();
    }

    internal ComposingTextBox Input { get; }

    internal RepeatButton Decrease { get; }

    internal RepeatButton Increase { get; }

    internal TextBlock Error { get; }

    /// <summary>The value last typed or stepped to that the range accepts; null until one is shown.</summary>
    public double? Value { get; private set; }

    /// <summary>Whether the text is not a number the range accepts, which the message under it says.</summary>
    public bool HasError => Error.IsVisible;

    /// <summary>A new valid value, typed or stepped to; never raised by <see cref="Show"/>.</summary>
    public event Action<double>? Committed;

    /// <summary>The text changed, valid or not.</summary>
    public event Action? Changed;

    /// <summary>Shows a value the owner holds, replacing the text and any message.</summary>
    public void Show(double value)
    {
        _showing = true;
        Value = value;
        Input.Text = NumberRange.Format(value, Localizer.Current.Culture);
        _showing = false;
        Error.IsVisible = false;
        UpdateButtons();
    }

    private void Read()
    {
        var culture = Localizer.Current.Culture;
        var value = _showing ? Value : _range.Read(Input.Text ?? string.Empty, culture);
        Error.IsVisible = value is null;
        Error.Text = value is null
            ? Localizer.T(_range.Whole ? "numberField.wholeRange" : "numberField.range",
                ("min", NumberRange.Format(_range.Min, culture)), ("max", NumberRange.Format(_range.Max, culture)))
            : string.Empty;
        if (value is { } valid && !_showing)
        {
            Value = valid;
            UpdateButtons();
            Committed?.Invoke(valid);
        }

        if (!_showing)
            Changed?.Invoke();
    }

    private RepeatButton StepButton(string glyph, string nameKey, int direction)
    {
        var button = new RepeatButton
        {
            Content = new PathIcon { Width = 10, Height = 10, Data = Geometry.Parse(glyph) },
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add("utility");
        button.Bind(WidthProperty, button.GetResourceObservable("ControlHeight"));
        Localized.SetAutomationName(button, nameKey);
        button.Click += (_, _) =>
        {
            if (Value is not { } from)
                return;
            var next = _range.Step(from, _step * direction);
            if (next == from)
                return;
            Show(next);
            Committed?.Invoke(next);
            Changed?.Invoke();
        };
        return button;
    }

    private void UpdateButtons()
    {
        Decrease.IsEnabled = Value is { } value && value > _range.Min;
        Increase.IsEnabled = Value is { } current && current < _range.Max;
    }
}
