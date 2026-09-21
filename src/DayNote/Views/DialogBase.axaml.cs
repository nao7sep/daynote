using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DayNote.Views;

/// <summary>
/// Base for the application's own modal dialogs (ported from the house pattern): a borderless,
/// owner-centred window with a content area and a right-aligned button row. The clicked button's
/// tag is exposed as <see cref="ResultTag"/>. Escape closes the dialog.
/// <para>
/// Copy-on-edit model: dialogs that edit durable data (e.g. <see cref="SettingsDialog"/>) mutate a
/// throwaway copy the caller adopts only on confirm, so closing without confirming simply discards
/// that copy. There is deliberately no dirty-close prompt here — do not add one.
/// </para>
/// </summary>
public partial class DialogBase : Window
{
    private Control? _initialFocusControl;

    public DialogBase()
    {
        InitializeComponent();
        Opened += OnOpened;
        KeyDown += OnKeyDown;
    }

    public string? ResultTag { get; private set; }

    protected void SetContent(Control content) => DialogContent.Content = content;

    protected IReadOnlyDictionary<string, Button> SetButtons(IEnumerable<DialogButton> buttons)
    {
        ButtonPanel.Children.Clear();
        var created = new Dictionary<string, Button>();

        // Secondary (cancel/dismiss) actions sit before primary/destructive ones; the panel is right-aligned.
        foreach (var spec in buttons.OrderBy(b => b.Kind == DialogButtonKind.Secondary ? 0 : 1))
        {
            var button = new Button
            {
                Content = spec.Label,
                Tag = spec.Tag,
                MinWidth = 88,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };

            button.Classes.Add(spec.Kind switch
            {
                DialogButtonKind.Primary => "accent",
                DialogButtonKind.Destructive => "destructive",
                _ => "utility",
            });
            button.Click += OnButtonClick;
            ButtonPanel.Children.Add(button);
            created[spec.Tag] = button;
        }

        return created;
    }

    protected void SetInitialFocus(Control control) => _initialFocusControl = control;

    /// <summary>
    /// Shows the dialog modally over <paramref name="owner"/>, bounded to a share of the owner's
    /// content height before the window is placed (modal-dialog conventions). Every owned dialog
    /// opens through here.
    /// </summary>
    public Task ShowBoundedAsync(Window owner)
    {
        BoundHeight(owner);
        return ShowDialog(owner);
    }

    /// <summary>
    /// Bounds a dialog that will be shown without an owner — the startup-failure shell, which is the
    /// application's only window — to the screen alone. Call it before the window is shown.
    /// </summary>
    protected void BoundHeightToScreen() => BoundHeight(null);

    private void BoundHeight(Window? owner)
    {
        // Before Show this window has no screen of its own, so the owner's is the one it will open on.
        var screen = owner is null
            ? Screens.Primary
            : owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;

        MaxHeight = WindowMetrics.DialogMaxHeight(
            owner?.ClientSize.Height ?? 0,
            screen?.WorkingArea.Height ?? 0,
            screen?.Scaling ?? 0);
    }

    /// <summary>
    /// Hands a resizable dialog's height to the user, once it has opened at the bound size
    /// (modal-dialog conventions). Runs after the window is sized and placed, so lifting the bound
    /// moves nothing and the minimums take over keeping the footer reachable.
    /// </summary>
    private void ReleaseHeightToUser()
    {
        var chrome = Bounds.Height - DialogScroll.Bounds.Height;

        SizeToContent = SizeToContent.Manual;
        MaxHeight = double.PositiveInfinity;
        MinHeight = WindowMetrics.DialogMinHeight(chrome);
        MinWidth = Bounds.Width;
    }

    /// <summary>Allows a feature dialog to finish its commit before the shell closes.</summary>
    protected virtual bool TryCommit(string tag) => true;

    private void OnOpened(object? sender, EventArgs e)
    {
        // A dialog declares itself resizable by setting CanResize; the shell makes that mean something.
        if (CanResize)
        {
            ReleaseHeightToUser();
        }

        if (_initialFocusControl is not null)
        {
            Dispatcher.UIThread.Post(() => _initialFocusControl.Focus());
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            var tag = button.Tag as string;
            if (tag is null || !TryCommit(tag))
            {
                return;
            }

            ResultTag = tag;
            Close();
        }
    }
}

/// <summary>How a dialog footer button is styled and treated.</summary>
public enum DialogButtonKind
{
    /// <summary>The affirmative default action (accent styling): OK, Save, Close, the safe primary.</summary>
    Primary,

    /// <summary>A cancel/dismiss action (utility styling). When a destructive button is present, the
    /// first Secondary is the safe initial focus.</summary>
    Secondary,

    /// <summary>A dangerous, irreversible commit (danger styling): Delete, Remove, Discard.</summary>
    Destructive,
}

/// <summary>A dialog footer button: its label, the <see cref="DialogBase.ResultTag"/> it yields, and its kind.</summary>
public sealed record DialogButton(string Label, string Tag, DialogButtonKind Kind = DialogButtonKind.Secondary);
