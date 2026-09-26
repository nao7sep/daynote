using Avalonia.Controls;
using Avalonia.Media;
using DayNote.I18n;

namespace DayNote.Views;

/// <summary>
/// A simple text dialog with a configurable button row, used for confirmations, errors, and choices.
/// Its title and message are rendered once, as it is built: it is modal, so the language cannot change
/// while it is up.
/// </summary>
public sealed class MessageDialog : DialogBase
{
    public MessageDialog(
        Message title,
        Message message,
        IReadOnlyList<DialogButton> buttons,
        double width = 440)
    {
        Title = Localizer.Of(title);
        Width = width;

        SetContent(new TextBlock
        {
            Text = Localizer.Of(message),
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

    /// <summary>
    /// A startup failure notice used as the main window. It has no owner to be bounded by, so the
    /// screen bounds it, and the lifetime shows it once it is returned.
    /// </summary>
    public static Window CreateStartupFailure(Message title, Message message)
    {
        var dialog = new MessageDialog(
            title,
            message,
            [new DialogButton("common.close", "close", DialogButtonKind.Primary)]);
        dialog.BoundHeightToScreen();
        ShowAsOnlyWindow(dialog);
        return dialog;
    }
}
