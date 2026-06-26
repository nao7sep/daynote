using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DayNote.Controls;

/// <summary>
/// DayNote's one text-entry control, IME-correct by construction so the text-input-ime-conventions
/// hold the same way on every field — note title and body, the binder-rename field, and the list
/// filters all use it. It owns both composition rules in a single place rather than scattering them:
///
/// <para><b>Enter is a submit only when it is a real Enter.</b> A key consumed by the input method
/// arrives as <see cref="Key.ImeProcessed"/>, so an Enter that merely commits a candidate raises
/// nothing here. Single-line fields (<see cref="TextBox.AcceptsReturn"/> false) raise
/// <see cref="Submitted"/>; the multiline body (AcceptsReturn true) lets Enter insert a newline.</para>
///
/// <para><b><see cref="IsComposing"/> reports composition as state,</b> read from the presenter's
/// preedit buffer. A window-level command accelerator (Cmd/Ctrl+S and the rest) is a chord the IME
/// passes straight through — it never arrives as <see cref="Key.ImeProcessed"/> — so suppressing it
/// mid-composition needs this persistent flag, not the per-key signal above.
/// <see cref="IsFocusedElementComposing"/> is how the window asks the question once.</para>
/// </summary>
public class ComposingTextBox : TextBox
{
    // A TextBox subclass would otherwise look up a control theme keyed by its own type, find none, and
    // render with no template (zero size / invisible). Borrow TextBox's theme so it looks like one.
    protected override Type StyleKeyOverride => typeof(TextBox);

    // The template's text presenter, which holds the live IME preedit (composition) buffer.
    private TextPresenter? _presenter;

    public static readonly RoutedEvent<RoutedEventArgs> SubmittedEvent =
        RoutedEvent.Register<ComposingTextBox, RoutedEventArgs>(nameof(Submitted), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> Submitted
    {
        add => AddHandler(SubmittedEvent, value);
        remove => RemoveHandler(SubmittedEvent, value);
    }

    /// <summary>
    /// True while an IME composition is in progress — a pending, not-yet-committed candidate sits in
    /// the preedit buffer. The candidate is not in <see cref="TextBox.Text"/> yet, so any command that
    /// reads or replaces the text while this holds would act on stale content.
    /// </summary>
    public bool IsComposing => !string.IsNullOrEmpty(_presenter?.PreeditText);

    /// <summary>
    /// True when the element holding keyboard focus is a <see cref="ComposingTextBox"/> that is
    /// mid-composition. The window consults this before running a command accelerator so a chord typed
    /// into a pending candidate is left to the IME instead of firing on not-yet-committed text.
    /// </summary>
    public static bool IsFocusedElementComposing(TopLevel topLevel) =>
        topLevel.FocusManager?.GetFocusedElement() is ComposingTextBox { IsComposing: true };

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
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
