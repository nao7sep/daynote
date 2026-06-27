using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class AttachmentReorderTests
{
    [Theory]
    [InlineData(0, 0.0, 5)] // no travel -> stays put
    [InlineData(0, 60.0, 3)] // 60px over 20px rows = 3 rows down, clamped to last (2)
    [InlineData(2, -40.0, 5)] // two rows up
    [InlineData(0, -100.0, 5)] // clamps at the top
    [InlineData(4, 100.0, 5)] // clamps at the bottom
    public void TargetIndex_shifts_by_whole_rows_and_clamps(int start, double delta, int count)
    {
        const double rowStep = 20.0;
        var expected = System.Math.Clamp(
            start + (int)System.Math.Round(delta / rowStep, System.MidpointRounding.AwayFromZero),
            0,
            count - 1);
        Assert.Equal(expected, AttachmentReorder.TargetIndex(start, delta, rowStep, count));
    }

    [Fact]
    public void TargetIndex_rounds_half_away_from_zero()
    {
        // Half a row down rounds to a full row (away from zero), not toward even.
        Assert.Equal(1, AttachmentReorder.TargetIndex(0, 10.0, 20.0, 5));
        Assert.Equal(1, AttachmentReorder.TargetIndex(2, -10.0, 20.0, 5));
    }

    [Fact]
    public void TargetIndex_does_not_move_when_row_step_is_unmeasurable()
    {
        Assert.Equal(3, AttachmentReorder.TargetIndex(3, 999.0, 0.0, 8));
        Assert.Equal(3, AttachmentReorder.TargetIndex(3, 999.0, -5.0, 8));
    }

    [Fact]
    public void TargetIndex_is_zero_for_an_empty_list()
    {
        Assert.Equal(0, AttachmentReorder.TargetIndex(0, 50.0, 20.0, 0));
    }
}
