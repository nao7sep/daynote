using Avalonia.Input;
using Avalonia.Layout;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class SplitterKeyboardTests
{
    [Theory]
    [InlineData(Key.Right, 380.0, true, 396.0)]   // a pane left of the splitter grows as it moves right
    [InlineData(Key.Left, 380.0, true, 364.0)]
    [InlineData(Key.Right, 380.0, false, 364.0)]  // a pane right of it shrinks
    [InlineData(Key.Home, 380.0, true, 320.0)]
    [InlineData(Key.End, 380.0, true, 640.0)]
    [InlineData(Key.Home, 380.0, false, 640.0)]
    [InlineData(Key.Left, 330.0, true, 320.0)]    // clamped at the minimum
    [InlineData(Key.Right, 630.0, true, 640.0)]   // and the maximum
    public void ColumnSplitter_MovesBySixteenOrToAnEnd(Key key, double size, bool paneBefore, double expected)
    {
        Assert.Equal(expected, SplitterKeyboard.Resize(key, Orientation.Horizontal, size, 320, 640, paneBefore));
    }

    [Theory]
    [InlineData(Key.Down, 200.0, 184.0)] // the console below its splitter shrinks as the splitter moves down
    [InlineData(Key.Up, 200.0, 216.0)]
    public void RowSplitter_TakesUpAndDown(Key key, double size, double expected)
    {
        Assert.Equal(expected, SplitterKeyboard.Resize(key, Orientation.Vertical, size, 100, 400, paneBefore: false));
    }

    [Theory]
    [InlineData(Key.Up, Orientation.Horizontal)]
    [InlineData(Key.Left, Orientation.Vertical)]
    [InlineData(Key.A, Orientation.Horizontal)]
    public void OtherKeys_AreLeftToTheWindow(Key key, Orientation axis)
    {
        Assert.Null(SplitterKeyboard.Resize(key, axis, 300, 100, 400, paneBefore: true));
    }
}
