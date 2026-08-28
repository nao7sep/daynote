using Avalonia;
using Avalonia.Input;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class AttachmentReorderTests
{
    [Theory]
    [InlineData(2.9, 0, false)]
    [InlineData(0, -2.9, false)]
    [InlineData(3, 0, true)]
    [InlineData(0, -3, true)]
    [InlineData(20, 20, true)]
    public void Drag_threshold_preserves_clicks_and_recognizes_pointer_intent(
        double x,
        double y,
        bool expected)
    {
        Assert.Equal(expected, AttachmentReorder.ExceedsDragThreshold(default, new Point(x, y)));
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
