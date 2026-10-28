using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using DayNote.I18n;

namespace DayNote.Views;

/// <summary>
/// Shown while a binder switch, close or removal, or a quit, waits for attachments still being added. It
/// closes itself once they settle. Waiting is the safe course, so it cannot be dismissed; after a few
/// seconds it offers Stop, which stops the adds and stays until the files in progress return.
/// </summary>
public sealed class AttachmentWaitDialog : DialogBase
{
    public const string StopTag = "stop";
    public const string SettledTag = "settled";

    private readonly Action _stop;
    private readonly Button _stopButton;

    public AttachmentWaitDialog(Task settled, TimeSpan stopOfferedAfter, Action stop)
    {
        _stop = stop;
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

        _stopButton = SetButtons([new DialogButton("attachments.stop", StopTag)])[StopTag];
        _stopButton.IsVisible = false;

        var offer = new DispatcherTimer { Interval = stopOfferedAfter };
        offer.Tick += (_, _) =>
        {
            offer.Stop();
            _stopButton.IsVisible = true;
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

    // Stop, its one button, does not close the dialog: the adds end once their files in progress return,
    // and the dialog closes with them.
    protected override bool TryCommit(string tag)
    {
        _stopButton.IsEnabled = false;
        _stop();
        return false;
    }
}
