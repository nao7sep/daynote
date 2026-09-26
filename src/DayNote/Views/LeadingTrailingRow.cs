using Avalonia;
using Avalonia.Controls;

namespace DayNote.Views;

/// <summary>
/// Two controls on one line, the first at the leading edge and the second at the trailing edge, as
/// long as both fit; when they do not, the second moves to a line of its own below, still at the
/// trailing edge. A longer language then gets a second line instead of labels laid over each other,
/// and a language that fits keeps the one-line layout unchanged.
/// </summary>
internal sealed class LeadingTrailingRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<LeadingTrailingRow, double>(nameof(Spacing));

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<LeadingTrailingRow, double>(nameof(LineSpacing));

    static LeadingTrailingRow() => AffectsMeasure<LeadingTrailingRow>(SpacingProperty, LineSpacingProperty);

    public LeadingTrailingRow(Control leading, Control trailing)
    {
        Children.Add(leading);
        Children.Add(trailing);
    }

    /// <summary>The room between the two controls when they share a line.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>The room between the two lines when the second control moves down.</summary>
    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    private Control Leading => Children[0];

    private Control Trailing => Children[1];

    private bool FitsOnOneLine(double width) =>
        Leading.DesiredSize.Width + Spacing + Trailing.DesiredSize.Width <= width;

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        Leading.Measure(unbounded);
        Trailing.Measure(unbounded);

        return FitsOnOneLine(availableSize.Width)
            ? new Size(
                Leading.DesiredSize.Width + Spacing + Trailing.DesiredSize.Width,
                Math.Max(Leading.DesiredSize.Height, Trailing.DesiredSize.Height))
            : new Size(
                Math.Max(Leading.DesiredSize.Width, Trailing.DesiredSize.Width),
                Leading.DesiredSize.Height + LineSpacing + Trailing.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var leading = Leading.DesiredSize;
        var trailing = Trailing.DesiredSize;
        if (FitsOnOneLine(finalSize.Width))
        {
            var height = Math.Max(leading.Height, trailing.Height);
            Leading.Arrange(new Rect(0, (height - leading.Height) / 2, leading.Width, leading.Height));
            Trailing.Arrange(new Rect(finalSize.Width - trailing.Width, (height - trailing.Height) / 2, trailing.Width, trailing.Height));
        }
        else
        {
            Leading.Arrange(new Rect(0, 0, Math.Min(leading.Width, finalSize.Width), leading.Height));
            Trailing.Arrange(new Rect(
                Math.Max(0, finalSize.Width - trailing.Width),
                leading.Height + LineSpacing,
                Math.Min(trailing.Width, finalSize.Width),
                trailing.Height));
        }

        return finalSize;
    }
}
