using Avalonia.Input;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class AttachmentReorderTests
{
    [Theory]
    [InlineData(10, 200, 30, -10)]
    [InlineData(100, 200, 30, 0)]
    [InlineData(195, 200, 30, 10)]
    [InlineData(10, 0, 30, 0)]
    [InlineData(10, 200, 0, 0)]
    public void EdgeScrollDelta_is_bounded_to_the_visible_edges(
        double pointerY,
        double viewportHeight,
        double rowStep,
        double expected)
    {
        Assert.Equal(expected, AttachmentReorder.EdgeScrollDelta(pointerY, viewportHeight, rowStep));
    }

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

    [Theory]
    [InlineData(Key.Up, KeyModifiers.Meta | KeyModifiers.Shift, -1)]
    [InlineData(Key.Down, KeyModifiers.Control | KeyModifiers.Shift, 1)]
    [InlineData(Key.Up, KeyModifiers.None, 0)]
    [InlineData(Key.Down, KeyModifiers.Shift, 0)]
    [InlineData(Key.Down, KeyModifiers.Meta | KeyModifiers.Shift | KeyModifiers.Alt, 0)]
    [InlineData(Key.ImeProcessed, KeyModifiers.Meta | KeyModifiers.Shift, 0)]
    public void KeyboardOffset_keeps_bare_navigation_and_IME_out_of_the_reorder_command(
        Key key,
        KeyModifiers modifiers,
        int expected)
    {
        Assert.Equal(expected, AttachmentReorder.KeyboardOffset(key, modifiers));
    }

    [Fact]
    public void KeyboardLabel_uses_the_running_platform_command_word_once()
    {
        Assert.Equal("Cmd+Shift+Up/Down", AttachmentReorder.KeyboardLabel("Cmd"));
        Assert.Equal("Ctrl+Shift+Up/Down", AttachmentReorder.KeyboardLabel("Ctrl"));
    }
}
