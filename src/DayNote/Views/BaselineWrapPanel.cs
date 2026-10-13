using Avalonia;
using Avalonia.Controls;

namespace DayNote.Views;

/// <summary>
/// A line of labels that wraps like a <see cref="WrapPanel"/> but puts every text on one baseline.
/// Separate text blocks centred in a line land on different baselines when their fonts differ, as a
/// count in Latin digits beside a Japanese date does: each block's line box comes from the fonts it
/// actually draws with. Here each line's texts are moved until their first baselines meet, and a
/// child with no text (a dot, a rule) is centred in the line. A nested panel aligns by its own first
/// line, so a group that must stay together keeps the shared baseline.
///
/// Children are placed in order; those marked <see cref="AtEndProperty"/> are pushed to the line's
/// trailing edge, so one row can hold a leading group and a trailing one, and whatever wraps stays
/// on the side it belongs to.
/// </summary>
internal sealed class BaselineWrapPanel : Panel
{
    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<BaselineWrapPanel, double>(nameof(ItemSpacing));

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<BaselineWrapPanel, double>(nameof(LineSpacing));

    public static readonly AttachedProperty<bool> AtEndProperty =
        AvaloniaProperty.RegisterAttached<BaselineWrapPanel, Control, bool>("AtEnd");

    static BaselineWrapPanel()
    {
        AffectsMeasure<BaselineWrapPanel>(ItemSpacingProperty, LineSpacingProperty);
        AffectsParentMeasure<BaselineWrapPanel>(AtEndProperty);
    }

    /// <summary>The room between neighbouring children on a line.</summary>
    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    /// <summary>The room between lines.</summary>
    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    public static bool GetAtEnd(Control control) => control.GetValue(AtEndProperty);

    public static void SetAtEnd(Control control, bool value) => control.SetValue(AtEndProperty, value);

    /// <summary>The first line's baseline from this panel's top, or NaN while it shows no text.</summary>
    public double FirstBaseline { get; private set; } = double.NaN;

    private sealed record Line(List<Control> Children, double Top, double Height, double Baseline);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }

        var lines = Lay(availableSize.Width);
        FirstBaseline = lines.Count == 0 ? double.NaN : lines[0].Baseline;
        var width = lines.Count == 0 ? 0 : lines.Max(LineWidth);
        var height = lines.Count == 0 ? 0 : lines[^1].Top + lines[^1].Height;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var line in Lay(finalSize.Width))
        {
            var leading = line.Children.Where(child => !GetAtEnd(child)).ToList();
            var trailing = line.Children.Where(GetAtEnd).ToList();
            var x = 0.0;
            foreach (var child in leading)
            {
                Place(child, x, line);
                x += child.DesiredSize.Width + ItemSpacing;
            }

            x = finalSize.Width - Span(trailing);
            foreach (var child in trailing)
            {
                Place(child, x, line);
                x += child.DesiredSize.Width + ItemSpacing;
            }
        }

        return finalSize;
    }

    private static void Place(Control child, double x, Line line)
    {
        var size = child.DesiredSize;
        var baseline = BaselineOf(child);
        var y = double.IsNaN(baseline) ? (line.Height - size.Height) / 2 : line.Baseline - baseline;
        child.Arrange(new Rect(x, line.Top + y, size.Width, size.Height));
    }

    // Greedy filling in child order; each line is as tall as its texts' highest ascent and lowest
    // descent together, or its tallest textless child, whichever is more.
    private List<Line> Lay(double width)
    {
        var groups = new List<List<Control>>();
        var current = new List<Control>();
        foreach (var child in Children.Where(child => child.IsVisible))
        {
            if (current.Count > 0 && Span(current) + ItemSpacing + child.DesiredSize.Width > width)
            {
                groups.Add(current);
                current = [];
            }

            current.Add(child);
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        var lines = new List<Line>();
        var top = 0.0;
        foreach (var group in groups)
        {
            double above = 0, below = 0, plain = 0;
            foreach (var child in group)
            {
                var baseline = BaselineOf(child);
                if (double.IsNaN(baseline))
                {
                    plain = Math.Max(plain, child.DesiredSize.Height);
                }
                else
                {
                    above = Math.Max(above, baseline);
                    below = Math.Max(below, child.DesiredSize.Height - baseline);
                }
            }

            var height = Math.Max(above + below, plain);
            lines.Add(new Line(group, top, height, (height - above - below) / 2 + above));
            top += height + LineSpacing;
        }

        return lines;
    }

    private double Span(List<Control> children) =>
        children.Count == 0 ? 0 : children.Sum(child => child.DesiredSize.Width) + ItemSpacing * (children.Count - 1);

    private double LineWidth(Line line) => Span(line.Children);

    /// <summary>A child's first baseline from the top of its desired box, or NaN when it shows no text.</summary>
    internal static double BaselineOf(Control child) => child switch
    {
        TextBlock { TextLayout.TextLines.Count: > 0 } text when !string.IsNullOrEmpty(text.Text) =>
            text.Margin.Top + text.Padding.Top + text.TextLayout.TextLines[0].Baseline,
        BaselineWrapPanel panel when !double.IsNaN(panel.FirstBaseline) => panel.Margin.Top + panel.FirstBaseline,
        _ => double.NaN,
    };
}
