using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DayNote.Services;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The two dialogs the user cannot dismiss (modal-dialog conventions): the question about a binder changed
/// outside DayNote, where each answer gives up a version, and the wait for attachments still being added,
/// where waiting is the safe course until Stop is offered.
/// </summary>
public sealed class BlockingDialogTests : IDisposable
{
    private readonly List<Window> _open = [];

    private (DialogService Service, Window Owner) Service()
    {
        var owner = new Window { Width = 900, Height = 600 };
        _open.Add(owner);
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        return (new DialogService(new NullLogger()) { Owner = owner }, owner);
    }

    private static Window OpenDialog(Window owner)
    {
        Dispatcher.UIThread.RunJobs();
        return Assert.Single(owner.OwnedWindows);
    }

    private static Button ButtonTagged(Window dialog, string tag) =>
        dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, tag));

    private static void Press(Window dialog, Key key, PhysicalKey physical)
    {
        dialog.KeyPress(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task The_outside_change_question_focuses_no_answer_and_escape_enter_or_close_answer_nothing()
    {
        var (service, owner) = Service();
        var asking = service.AskExternalChangeAsync("Journal");
        var dialog = OpenDialog(owner);

        Assert.Null(dialog.FocusManager?.GetFocusedElement() as Button);
        Press(dialog, Key.Escape, PhysicalKey.Escape);
        Press(dialog, Key.Enter, PhysicalKey.Enter);
        dialog.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(asking.IsCompleted);

        ButtonTagged(dialog, "keep").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ExternalChangeChoice.KeepMine, await asking);
    }

    [AvaloniaFact]
    public async Task The_outside_change_question_reloads_when_asked_and_closes_unanswered_with_its_owner()
    {
        var (service, owner) = Service();
        var reloading = service.AskExternalChangeAsync("Journal");
        ButtonTagged(OpenDialog(owner), "reload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ExternalChangeChoice.ReloadFromDisk, await reloading);

        var asking = service.AskExternalChangeAsync("Journal");
        OpenDialog(owner);
        owner.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ExternalChangeChoice.Unanswered, await asking);
    }

    [AvaloniaFact]
    public async Task The_attachment_wait_cannot_be_dismissed_and_closes_itself_once_the_adds_settle()
    {
        var (service, owner) = Service();
        var settled = new TaskCompletionSource();
        var waiting = service.WaitForAttachmentsAsync(settled.Task, TimeSpan.FromHours(1));
        var dialog = OpenDialog(owner);

        // Stop is not offered yet, and nothing else ends the wait.
        Assert.False(ButtonTagged(dialog, AttachmentWaitDialog.StopTag).IsVisible);
        Press(dialog, Key.Escape, PhysicalKey.Escape);
        dialog.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(waiting.IsCompleted);

        settled.SetResult();
        Assert.False(await waiting);
        Assert.Empty(owner.OwnedWindows);
    }

    [AvaloniaFact]
    public async Task The_attachment_wait_offers_stop_after_its_delay()
    {
        var (service, owner) = Service();
        var waiting = service.WaitForAttachmentsAsync(new TaskCompletionSource().Task, TimeSpan.FromMilliseconds(1));
        var dialog = OpenDialog(owner);
        var stop = ButtonTagged(dialog, AttachmentWaitDialog.StopTag);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!stop.IsVisible && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(stop.IsVisible);
        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await waiting);
    }

    [AvaloniaFact]
    public async Task The_quit_question_offers_cancel_focused_and_it_keeps_the_app_open()
    {
        var (service, owner) = Service();
        var asking = service.AskQuitWithUnsavedBinderAsync("Journal");
        var dialog = OpenDialog(owner);
        var cancel = ButtonTagged(dialog, "cancel");

        Assert.Same(cancel, dialog.FocusManager?.GetFocusedElement());
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(UnsavedQuitChoice.Stay, await asking);

        var retrying = service.AskQuitWithUnsavedBinderAsync("Journal");
        ButtonTagged(OpenDialog(owner), "retry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(UnsavedQuitChoice.Retry, await retrying);
    }

    [AvaloniaFact]
    public void The_session_end_save_loop_ends_when_its_work_finishes_on_another_thread()
    {
        var work = Task.Run(() => Task.Delay(20));

        MainWindow.RunUntilDone(work);

        Assert.True(work.IsCompleted);
    }

    [AvaloniaFact]
    public async Task An_attachment_wait_for_adds_already_settled_shows_nothing()
    {
        var (service, owner) = Service();

        Assert.False(await service.WaitForAttachmentsAsync(Task.CompletedTask, TimeSpan.FromHours(1)));
        Assert.Empty(owner.OwnedWindows);
    }

    public void Dispose()
    {
        for (var i = _open.Count - 1; i >= 0; i--)
        {
            _open[i].Close();
        }

        _open.Clear();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class NullLogger : DayNote.Logging.IAppLogger
    {
        public void Debug(string message, object? data = null, Exception? error = null) { }
        public void Info(string message, object? data = null, Exception? error = null) { }
        public void Warn(string message, object? data = null, Exception? error = null) { }
        public void Error(string message, object? data = null, Exception? error = null) { }
    }
}
