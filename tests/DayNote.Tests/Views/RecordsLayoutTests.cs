using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class RecordsLayoutTests
{
    [Fact]
    public void The_window_minimum_is_both_pane_minimums_and_what_sits_beside_them()
    {
        // Grid margin, list minimum, splitter, detail minimum.
        Assert.Equal(8 + 320 + 6 + 420, RecordsLayout.MinWidth);
        Assert.True(RecordsLayout.MinHeight(140) > 140 + RecordsLayout.ListBodyMin);
    }

    [Theory]
    [InlineData(380, 1100, 380)]
    [InlineData(900, 1400, 640)]
    [InlineData(100, 1100, 320)]
    // The detail pane keeps its minimum: 800 - 8 - 6 - 420 leaves the list 366.
    [InlineData(500, 800, 366)]
    // Below the window minimum the list still keeps its own.
    [InlineData(500, 600, 320)]
    public void The_list_shows_its_intent_within_its_bounds_and_what_the_detail_pane_leaves(double intent, double window, double shown)
    {
        Assert.Equal(shown, RecordsLayout.ListWidth(intent, window));
    }

    [Fact]
    public void A_width_that_is_not_a_number_opens_at_the_default()
    {
        Assert.Equal(RecordsLayout.ListDefault, RecordsLayout.ListWidth(double.NaN, 1100));
    }
}
