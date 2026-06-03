using Avalonia.Controls;
using Avalonia.Media;

namespace DayNote.Desktop.Views;

/// <summary>A simple text dialog with a configurable button row, used for confirmations, errors, and choices.</summary>
public sealed class MessageDialog : DialogBase
{
    public MessageDialog(
        string title,
        string message,
        IReadOnlyList<(string Label, string Tag, bool IsDefault)> buttons,
        double width = 440)
    {
        Title = title;
        Width = width;

        SetContent(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            MaxWidth = width - 48,
        });

        var created = SetButtons(buttons);
        foreach (var button in buttons)
        {
            if (button.IsDefault && created.TryGetValue(button.Tag, out var control))
            {
                SetInitialFocus(control);
            }
        }
    }
}
