using Avalonia.Controls;
using Avalonia.Media;

namespace DayNote.Desktop.Views;

/// <summary>A simple text dialog with a configurable button row, used for confirmations, errors, and choices.</summary>
public sealed class MessageDialog : DialogBase
{
    public MessageDialog(
        string title,
        string message,
        IReadOnlyList<DialogButton> buttons,
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

        // Focus the safest action: when a destructive button is present, the cancel/secondary one, so a
        // stray Space/Enter never lands on the dangerous action; otherwise the primary.
        var focusTag = buttons.Any(b => b.Kind == DialogButtonKind.Destructive)
            ? buttons.FirstOrDefault(b => b.Kind == DialogButtonKind.Secondary)?.Tag
            : buttons.FirstOrDefault(b => b.Kind == DialogButtonKind.Primary)?.Tag;
        focusTag ??= buttons.Count > 0 ? buttons[0].Tag : null;
        if (focusTag is not null && created.TryGetValue(focusTag, out var focus))
        {
            SetInitialFocus(focus);
        }
    }
}
