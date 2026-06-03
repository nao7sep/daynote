using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DayNote.Desktop.Views;

/// <summary>
/// Base for the application's own modal dialogs (ported from the house pattern): a borderless,
/// owner-centred window with a content area and a right-aligned button row. The clicked button's
/// tag is exposed as <see cref="ResultTag"/>. Escape closes the dialog.
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

    protected IReadOnlyDictionary<string, Button> SetButtons(
        IEnumerable<(string Label, string Tag, bool IsDefault)> buttons)
    {
        ButtonPanel.Children.Clear();
        var created = new Dictionary<string, Button>();

        foreach (var (label, tag, isDefault) in buttons.OrderBy(button => button.IsDefault))
        {
            var button = new Button
            {
                Content = label,
                Tag = tag,
                MinWidth = 88,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };

            button.Classes.Add(isDefault ? "accent" : "utility");
            button.Click += OnButtonClick;
            ButtonPanel.Children.Add(button);
            created[tag] = button;
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
