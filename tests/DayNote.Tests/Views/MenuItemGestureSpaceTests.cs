using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.I18n;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// A menu item without a gesture ends as close to its label as it begins. Fluent reserves a 24px
/// margin for gesture text on every item, so the hamburger menu used to carry 34px after its widest
/// label against 10px before every label.
/// </summary>
public sealed class MenuItemGestureSpaceTests : WindowTest
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ru")]
    [InlineData("ja")]
    public void The_hamburger_menu_ends_as_close_to_its_widest_label_as_it_begins(string language)
    {
        using var speaking = Localizer.Speaking(language);
        var window = Show(new MainWindow());
        var hamburger = window.GetVisualDescendants().OfType<Button>().Single(button => button.Flyout is MenuFlyout);
        var flyout = (MenuFlyout)hamburger.Flyout!;
        flyout.ShowAt(hamburger);
        Dispatcher.UIThread.RunJobs();

        try
        {
            var gaps = flyout.Items.OfType<MenuItem>().Select(item => Gaps(item, window)).ToList();
            var leading = gaps.Min(gap => gap.Leading);
            var trailing = gaps.Min(gap => gap.Trailing);

            Assert.True(leading > 0);
            // The item's width is whole pixels around a fractional label, so the trailing side may
            // carry up to one pixel of rounding.
            Assert.InRange(trailing - leading, 0, 1);
        }
        finally
        {
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // A gesture keeps the template's space between the label and the gesture, and the space follows
    // the gesture when it is added or removed after the item is on screen.
    [AvaloniaFact]
    public void Only_an_item_without_a_gesture_drops_the_gesture_margin()
    {
        var withGesture = new MenuItem { Header = "Copy", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
        var without = new MenuItem { Header = "About" };
        var anchor = new Button { Content = "Menu" };
        var flyout = new MenuFlyout { Items = { withGesture, without } };
        Show(new Window { Content = anchor });
        flyout.ShowAt(anchor);
        Dispatcher.UIThread.RunJobs();

        try
        {
            var templateMargin = GestureText(withGesture).Margin;
            Assert.True(templateMargin.Left > 0);
            Assert.Equal(default, GestureText(without).Margin);

            withGesture.InputGesture = null;
            Assert.Equal(default, GestureText(withGesture).Margin);

            without.InputGesture = new KeyGesture(Key.A, KeyModifiers.Control);
            Assert.Equal(templateMargin, GestureText(without).Margin);
        }
        finally
        {
            flyout.Hide();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static TextBlock GestureText(MenuItem item) =>
        item.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_InputGestureText");

    // From the item's highlight edge to its label's text, on each side.
    private static (double Leading, double Trailing) Gaps(MenuItem item, Visual window)
    {
        var highlight = item.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_LayoutRoot");
        var label = item.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name != "PART_InputGestureText");
        var left = highlight.TranslatePoint(default, window)!.Value.X;
        var textLeft = label.TranslatePoint(default, window)!.Value.X;
        var textRight = textLeft + label.TextLayout.WidthIncludingTrailingWhitespace;
        return (textLeft - left, left + highlight.Bounds.Width - textRight);
    }
}
