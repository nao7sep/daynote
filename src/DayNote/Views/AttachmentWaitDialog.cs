using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using DayNote.I18n;

namespace DayNote.Views;

/// <summary>
/// Shown while a binder switch, close or removal, or a quit, waits for attachments still being added. It
/// closes itself once they settle. Waiting is the safe course, so it cannot be dismissed; after a few
/// seconds it offers Stop, so a drive that stops answering cannot keep the user waiting for good.
/// </summary>
public sealed class AttachmentWaitDialog : DialogBase
{
    public const string StopTag = "stop";
    public const string SettledTag = "settled";

    public AttachmentWaitDialog(Task settled, TimeSpan stopOfferedAfter)
    {
        Title = Localizer.T("attachments.waitTitle");
        Width = 440;
        IsDismissable = false;

        SetContent(new TextBlock
        {
            Text = Localizer.T("attachments.waitMessage"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            MaxWidth = 440 - 48,
        });

        var stop = SetButtons([new DialogButton("attachments.stop", StopTag)])[StopTag];
        stop.IsVisible = false;

        var offer = new DispatcherTimer { Interval = stopOfferedAfter };
        offer.Tick += (_, _) =>
        {
            offer.Stop();
            stop.IsVisible = true;
        };
        Opened += (_, _) => offer.Start();
        Closed += (_, _) => offer.Stop();

        settled.ContinueWith(
            _ => Dispatcher.UIThread.Post(() =>
            {
                if (ResultTag is null)
                {
                    CloseWith(SettledTag);
                }
            }),
            TaskScheduler.Default);
    }
}
