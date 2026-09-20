using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace DayNote.Tests;

public sealed class AppStylesTests
{
    [AvaloniaFact]
    public void Scrollbars_use_the_shared_native_hover_and_leave_timing()
    {
        var viewer = OverflowingViewer();
        var window = new Window { Content = viewer, Width = 120, Height = 120 };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var bars = viewer.GetVisualDescendants().OfType<ScrollBar>().ToList();

            Assert.True(viewer.AllowAutoHide);
            Assert.NotEmpty(bars);
            Assert.All(bars, bar =>
            {
                Assert.True(bar.AllowAutoHide);
                Assert.Equal(TimeSpan.Zero, bar.ShowDelay);
                Assert.Equal(TimeSpan.FromSeconds(2), bar.HideDelay);
            });
        }
        finally
        {
            window.Close();
        }
    }

    // A disabled button is the resting button, faded: the toolkit's own grey fill and grey ink never
    // reach it, so it stays the control it will be again. Left to Fluent, the accent button did not
    // change at all when disabled, the utility button became a slab heavier than its resting self,
    // and the danger button lost its fill, its outline and its red at once, leaving bare grey letters.
    [AvaloniaTheory]
    [InlineData("accent")]
    [InlineData("utility")]
    [InlineData("danger")]
    public void A_disabled_button_is_its_resting_self_faded(string variant)
    {
        var resting = Classed(variant, enabled: true);
        var off = Classed(variant, enabled: false);
        var window = new Window { Content = new StackPanel { Children = { resting, off } } };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1d, resting.Opacity);
            Assert.Equal(DisabledOpacity(), off.Opacity);
            Assert.Equal(Fill(resting), Fill(off));
            Assert.Equal(Ink(resting), Ink(off));
        }
        finally
        {
            window.Close();
        }
    }

    // The outline is what makes the destructive trigger a button rather than a line of text, and the
    // red is what says the action destroys something. Neither stops being true while it is unavailable.
    [AvaloniaFact]
    public void A_disabled_danger_button_keeps_its_red_outline()
    {
        var off = Classed("danger", enabled: false);
        var window = new Window { Content = off };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new Thickness(1), Presenter(off).BorderThickness);
            Assert.Equal(Color(Brush("DangerTextBrush")), Color(Presenter(off).BorderBrush));
        }
        finally
        {
            window.Close();
        }
    }

    // Pressed is a step of the control's own surface, distinct from both resting and hover. A class
    // that leaves it unsaid gets Fluent's grey under the finger instead — or, where an app style
    // already pins the fill, no change at all, which is a button that does not answer the click.
    [AvaloniaTheory]
    [InlineData("accent", "AccentPressedBrush")]
    [InlineData("utility", "UtilityPressedBrush")]
    [InlineData("destructive", "DangerPressedBrush")]
    [InlineData("danger", "DangerBrush")]
    public void A_pressed_button_is_a_step_of_its_own_surface(string variant, string pressedBrush)
    {
        var resting = Classed(variant, enabled: true);
        var hovered = Classed(variant, enabled: true);
        var pressed = Classed(variant, enabled: true);
        var window = new Window { Content = new StackPanel { Children = { resting, hovered, pressed } } };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            // The pseudo-classes are set after the template is applied; the control resets them on attach.
            ((IPseudoClasses)hovered.Classes).Set(":pointerover", true);
            ((IPseudoClasses)pressed.Classes).Set(":pressed", true);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Color(Brush(pressedBrush)), Fill(pressed));
            Assert.NotEqual(Fill(resting), Fill(pressed));
            Assert.NotEqual(Fill(hovered), Fill(pressed));
        }
        finally
        {
            window.Close();
        }
    }

    private static Button Classed(string variant, bool enabled)
    {
        var button = new Button { Content = "Remove", IsEnabled = enabled };
        button.Classes.Add(variant);
        return button;
    }

    private static ContentPresenter Presenter(Button button) =>
        button.GetVisualDescendants().OfType<ContentPresenter>().First();

    private static string Fill(Button button) => Color(Presenter(button).Background);

    private static string Ink(Button button) => Color(Presenter(button).Foreground);

    private static string Color(IBrush? brush) =>
        brush is ISolidColorBrush solid ? solid.Color.ToString() : $"<{brush?.GetType().Name ?? "null"}>";

    private static IBrush Brush(string key) => (IBrush)Resource(key);

    private static double DisabledOpacity() => (double)Resource("DisabledOpacity");

    private static object Resource(string key)
    {
        var app = Application.Current!;
        return app.FindResource(app.ActualThemeVariant, key)!;
    }

    private static ScrollViewer OverflowingViewer() => new()
    {
        Content = new Border { Width = 400, Height = 400 },
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
}
