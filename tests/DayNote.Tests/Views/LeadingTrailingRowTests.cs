using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The row that keeps two controls on one line while they fit, and moves the second to a line of its
/// own, still at the trailing edge, when they do not.
/// </summary>
public sealed class LeadingTrailingRowTests
{
    [AvaloniaFact]
    public void Both_share_a_line_at_opposite_edges_when_they_fit()
    {
        var (row, leading, trailing) = Row(leadingWidth: 100, trailingWidth: 120);

        Layout(row, width: 300);

        Assert.Equal(new Rect(0, 0, 100, 20), leading.Bounds);
        Assert.Equal(new Rect(180, 0, 120, 20), trailing.Bounds);
        Assert.Equal(20, row.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void The_second_takes_its_own_line_at_the_trailing_edge_when_they_do_not()
    {
        var (row, leading, trailing) = Row(leadingWidth: 200, trailingWidth: 120);

        Layout(row, width: 300);

        Assert.Equal(new Rect(0, 0, 200, 20), leading.Bounds);
        Assert.Equal(new Rect(180, 30, 120, 20), trailing.Bounds);
        Assert.Equal(50, row.DesiredSize.Height);
    }

    private static (LeadingTrailingRow Row, Control Leading, Control Trailing) Row(double leadingWidth, double trailingWidth)
    {
        var leading = new Border { Width = leadingWidth, Height = 20 };
        var trailing = new Border { Width = trailingWidth, Height = 20 };
        return (new LeadingTrailingRow(leading, trailing) { Spacing = 16, LineSpacing = 10 }, leading, trailing);
    }

    private static void Layout(Control row, double width)
    {
        row.Measure(new Size(width, double.PositiveInfinity));
        row.Arrange(new Rect(0, 0, width, row.DesiredSize.Height));
    }
}
