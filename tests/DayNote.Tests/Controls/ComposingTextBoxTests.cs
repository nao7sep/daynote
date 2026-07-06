using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Controls;
using Xunit;

namespace DayNote.Tests.Controls;

/// <summary>
/// The single IME-aware text input. Enter submits only when it is a real Enter, persistent preedit
/// state guards window accelerators, and the macOS client uses the same visual coordinate space as
/// its cursor rectangle. Composition is simulated through the public text-input client/presenter APIs
/// so no real input method is needed.
/// </summary>
public sealed class ComposingTextBoxTests
{
    // Showing the control realizes its template, which is what populates the box's presenter reference.
    private static T Host<T>(T content) where T : Control
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return content;
    }

    private static TextPresenter Presenter(ComposingTextBox box) =>
        box.GetVisualDescendants().OfType<TextPresenter>().Single(p => p.Name == "PART_TextPresenter");

    private static bool RaiseKeyDown(ComposingTextBox box, Key key)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
        box.RaiseEvent(e);
        return e.Handled;
    }

    private static TextInputMethodClient InputMethodClient(ComposingTextBox box)
    {
        var e = new TextInputMethodClientRequestedEventArgs
        {
            RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
        };
        box.RaiseEvent(e);
        return Assert.IsAssignableFrom<TextInputMethodClient>(e.Client);
    }

    [AvaloniaFact]
    public void Real_Enter_in_a_single_line_box_raises_Submitted()
    {
        var box = Host(new ComposingTextBox { AcceptsReturn = false });
        var submitted = 0;
        box.Submitted += (_, _) => submitted++;

        var handled = RaiseKeyDown(box, Key.Enter);

        Assert.Equal(1, submitted);
        Assert.True(handled);
    }

    [AvaloniaFact]
    public void Ime_commit_Enter_does_not_raise_Submitted()
    {
        // A key the input method consumed (Enter accepting a candidate) arrives as ImeProcessed, not Enter.
        var box = Host(new ComposingTextBox { AcceptsReturn = false });
        var submitted = 0;
        box.Submitted += (_, _) => submitted++;

        RaiseKeyDown(box, Key.ImeProcessed);

        Assert.Equal(0, submitted);
    }

    [AvaloniaFact]
    public void Enter_in_the_multiline_body_inserts_a_newline_not_a_submit()
    {
        var box = Host(new ComposingTextBox { AcceptsReturn = true });
        var submitted = 0;
        box.Submitted += (_, _) => submitted++;

        RaiseKeyDown(box, Key.Enter);

        Assert.Equal(0, submitted);
    }

    [AvaloniaFact]
    public void IsComposing_follows_the_preedit_buffer()
    {
        var box = Host(new ComposingTextBox());
        Assert.False(box.IsComposing);

        Presenter(box).PreeditText = "にほん";
        Assert.True(box.IsComposing);

        Presenter(box).PreeditText = null;
        Assert.False(box.IsComposing);
    }

    [AvaloniaFact]
    public void IsFocusedElementComposing_is_true_only_for_a_focused_composing_box()
    {
        var a = new ComposingTextBox();
        var b = new ComposingTextBox();
        var window = new Window { Content = new StackPanel { Children = { a, b } }, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        a.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(ComposingTextBox.IsFocusedElementComposing(window)); // focused, but no pending candidate

        Presenter(a).PreeditText = "か";
        Assert.True(ComposingTextBox.IsFocusedElementComposing(window));

        b.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.False(ComposingTextBox.IsFocusedElementComposing(window)); // the composing box no longer holds focus
    }

    [AvaloniaFact]
    public void MacOs_input_client_reports_the_text_box_as_its_coordinate_visual()
    {
        var box = Host(new ComposingTextBox());
        var client = InputMethodClient(box);

        if (OperatingSystem.IsMacOS())
        {
            Assert.Same(box, client.TextViewVisual);
        }
        else
        {
            Assert.Same(Presenter(box), client.TextViewVisual);
        }
    }

    [AvaloniaFact]
    public void MacOs_input_client_preserves_preedit_and_cursor_notifications()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var box = Host(new ComposingTextBox { Text = "abc" });
        var presenter = Presenter(box);
        var client = InputMethodClient(box);
        var cursorChanges = 0;
        client.CursorRectangleChanged += (_, _) => cursorChanges++;

        client.SetPreeditText("にほん", 2);

        Assert.Equal("にほん", presenter.PreeditText);
        Assert.True(cursorChanges > 0);
    }

    [AvaloniaFact]
    public void MacOs_input_client_refreshes_its_cursor_rectangle_when_the_text_box_scrolls()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var box = Host(new ComposingTextBox
        {
            AcceptsReturn = true,
            Height = 80,
            Text = string.Join('\n', Enumerable.Repeat("line", 30)),
        });
        var scrollViewer = box.GetVisualDescendants().OfType<ScrollViewer>().Single();
        var client = InputMethodClient(box);
        var cursorChanges = 0;
        client.CursorRectangleChanged += (_, _) => cursorChanges++;

        scrollViewer.Offset = new Avalonia.Vector(0, 100);
        Dispatcher.UIThread.RunJobs();

        Assert.True(cursorChanges > 0);
    }
}
