using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DayNote.Desktop.Controls;

/// <summary>
/// A <see cref="TextBox"/> that respects input-method composition, ported in spirit from quickdeck.
/// In Avalonia a key consumed by the IME arrives as <see cref="Key.ImeProcessed"/>, so the Enter
/// key is only treated as a submit when it is not part of an in-progress composition. Used for
/// single-line fields (such as the note title) where Enter has meaning; the body editor is a plain
/// multiline <see cref="TextBox"/> where Enter simply inserts a newline.
/// </summary>
public class ComposingTextBox : TextBox
{
    public static readonly RoutedEvent<RoutedEventArgs> SubmittedEvent =
        RoutedEvent.Register<ComposingTextBox, RoutedEventArgs>(nameof(Submitted), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> Submitted
    {
        add => AddHandler(SubmittedEvent, value);
        remove => RemoveHandler(SubmittedEvent, value);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // A key still being processed by the input method must not trigger actions.
        if (e.Key == Key.ImeProcessed)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.Key == Key.Enter && !AcceptsReturn)
        {
            RaiseEvent(new RoutedEventArgs(SubmittedEvent));
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
