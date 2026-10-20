using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class RecordsLayoutTests
{
    [Fact]
    public void The_window_minimum_is_both_pane_minimums_and_what_sits_beside_them()
    {
        // At the minimum width neither pane can grow past its minimum.
        Assert.Equal(RecordsLayout.ListMin, RecordsLayout.ListWidth(RecordsLayout.ListMax, RecordsLayout.MinWidth));
        Assert.True(RecordsLayout.MinWidth > RecordsLayout.ListMin + RecordsLayout.DetailMin);
        Assert.True(RecordsLayout.MinHeight(140) > 140 + RecordsLayout.ListBodyMin);
    }

    [Theory]
    [InlineData(RecordsLayout.ListDefault, 1100, RecordsLayout.ListDefault)]
    [InlineData(900, 1400, RecordsLayout.ListMax)]
    [InlineData(100, 1100, RecordsLayout.ListMin)]
    // Below the window minimum the list still keeps its own.
    [InlineData(500, 600, RecordsLayout.ListMin)]
    public void The_list_shows_its_intent_within_its_bounds_and_what_the_detail_pane_leaves(double intent, double window, double shown)
    {
        Assert.Equal(shown, RecordsLayout.ListWidth(intent, window));
    }

    [Fact]
    public void A_narrow_window_narrows_the_list_so_the_detail_pane_keeps_its_minimum()
    {
        // What sits beside the panes is what the minimum window adds to both pane minimums.
        var beside = RecordsLayout.MinWidth - RecordsLayout.ListMin - RecordsLayout.DetailMin;

        Assert.Equal(800 - beside - RecordsLayout.DetailMin, RecordsLayout.ListWidth(500, 800));
    }

    [Fact]
    public void A_width_that_is_not_a_number_opens_at_the_default()
    {
        Assert.Equal(RecordsLayout.ListDefault, RecordsLayout.ListWidth(double.NaN, 1100));
    }
}
