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

    private void OnOpened(object? sender, EventArgs e)
    {
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
            ResultTag = button.Tag as string;
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
