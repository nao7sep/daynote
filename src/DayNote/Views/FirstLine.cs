using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace DayNote.Views;

/// <summary>
/// A per-item X (a row's remove or delete, a result's close) sits on the item's first text line,
/// centred on it, however many lines follow or wrap. The button names that text through
/// <see cref="TextProperty"/>; after every layout pass it is shifted vertically so its centre is the
/// centre of the text's real first line (<c>TextLayout.TextLines[0]</c>). The shift is an equal and
/// opposite pair of top and bottom margins, which moves the button inside its slot without changing
/// how much room it takes. Only the X moves: the text keeps its own line height, padding and
/// position, and the button keeps its size, so the item is exactly as tall as before; the hit area
/// and focus ring move with it. A render translation would say the same, but Fluent animates a
/// button's RenderTransform, so the X would glide into place instead of standing there. Measuring
/// after layout covers everything that moves the line — the item's width, the text, the UI font and
/// its size, and a fallback font for another script — because each of those relays the text.
/// </summary>
public static class FirstLine
{
    /// <summary>The text whose first line the button sits on.</summary>
    public static readonly AttachedProperty<TextBlock?> TextProperty =
        AvaloniaProperty.RegisterAttached<Button, TextBlock?>("Text", typeof(FirstLine));

    static FirstLine() =>
        TextProperty.Changed.AddClassHandler<Button>((button, e) =>
        {
            if (e.OldValue is null)
                button.LayoutUpdated += (_, _) => Place(button);
        });

    public static TextBlock? GetText(Button button) => button.GetValue(TextProperty);

    public static void SetText(Button button, TextBlock? text) => button.SetValue(TextProperty, text);

    /// <summary>
    /// How far a box at <paramref name="boxTop"/>, <paramref name="boxHeight"/> tall, moves down so
    /// its centre is the centre of a line at <paramref name="lineTop"/>, <paramref name="lineHeight"/>
    /// tall, all in one coordinate space.
    /// </summary>
    public static double OffsetFor(double boxTop, double boxHeight, double lineTop, double lineHeight) =>
        lineTop + lineHeight / 2 - (boxTop + boxHeight / 2);

    private static void Place(Button button)
    {
        if (GetText(button) is not { IsVisible: true } text
            || button.GetVisualParent() is not Visual parent
            || text.TextLayout.TextLines.Count == 0
            || text.TranslatePoint(new Point(0, text.Padding.Top), parent) is not { } lineTop)
            return;

        // Where the button would stand unshifted: its place less the shift it already has.
        var unshiftedTop = button.Bounds.Y - button.Margin.Top;
        var offset = OffsetFor(unshiftedTop, button.Bounds.Height, lineTop.Y, text.TextLayout.TextLines[0].Height);
        // Layout rounding moves the button by up to half a pixel, which would otherwise read as a new
        // place and shift it again on every pass.
        if (Math.Abs(offset - button.Margin.Top) < 0.5)
            return;
        button.Margin = new Thickness(button.Margin.Left, offset, button.Margin.Right, -offset);
    }
}
