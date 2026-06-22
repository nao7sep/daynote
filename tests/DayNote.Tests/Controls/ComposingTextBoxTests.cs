using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Desktop.Controls;
using Xunit;

namespace DayNote.Tests.Controls;

/// <summary>
/// The single IME-aware text input. Two contracts are exercised: Enter is a submit only when it is a
/// real Enter (never the IME's candidate-commit, which arrives as <see cref="Key.ImeProcessed"/>), and
/// <see cref="ComposingTextBox.IsComposing"/> tracks the presenter's preedit buffer so a window
/// accelerator can stand down mid-composition. Composition is simulated by setting the preedit text
/// directly — a public presenter property — so no real input method is needed.
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
}
