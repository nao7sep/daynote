using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DayNote.I18n;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class DialogFailureTests : WindowTest
{
    [AvaloniaFact]
    public async Task A_failed_async_commit_keeps_the_dialog_open_and_allows_retry()
    {
        var failure = new InvalidOperationException("commit sentinel");
        var logged = new TaskCompletionSource<(string Message, Exception Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = Show(new FailingDialog { CommitFailure = failure });
        dialog.LogFailure = (message, error) => logged.TrySetResult((message, error));

        dialog.Save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var entry = await logged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Failed to commit dialog", entry.Message);
        Assert.Same(failure, entry.Error);
        Assert.True(dialog.IsVisible);
        Assert.True(dialog.IsEnabled);
        Assert.Null(dialog.ResultTag);

        dialog.CommitFailure = null;
        dialog.Save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        MainWindowQuitTests.PumpUntil(() => !dialog.IsVisible);
        Assert.Equal("save", dialog.ResultTag);
    }

    [AvaloniaFact]
    public async Task A_failed_async_discard_question_keeps_the_draft_open_and_allows_retry()
    {
        var failure = new InvalidOperationException("discard sentinel");
        var logged = new TaskCompletionSource<(string Message, Exception Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = Show(new FailingDialog { Unsaved = true, DiscardFailure = failure });
        dialog.LogFailure = (message, error) => logged.TrySetResult((message, error));
        try
        {
            dialog.Close();
            var entry = await logged.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("Failed to ask before discarding dialog changes", entry.Message);
            Assert.Same(failure, entry.Error);
            Assert.True(dialog.IsVisible);
            Assert.True(dialog.Unsaved);
            Assert.Null(dialog.ResultTag);

            dialog.DiscardFailure = null;
            dialog.Close();
            MainWindowQuitTests.PumpUntil(() => !dialog.IsVisible);
        }
        finally
        {
            dialog.Unsaved = false;
        }
    }

    [AvaloniaFact]
    public async Task A_stacked_dialog_inherits_its_owners_failure_logger()
    {
        var owner = Show(new FailingDialog());
        owner.LogFailure = (_, _) => { };
        var child = Track(new MessageDialog(Message.Of("common.cancel"), Message.Of("common.cancel"),
            [new DialogButton("common.ok", "ok")]));
        var showing = child.ShowBoundedAsync(owner);

        Assert.Same(owner.LogFailure, child.LogFailure);
        child.Close();
        await showing;
    }

    private sealed class FailingDialog : DialogBase
    {
        public FailingDialog() => Save = SetButtons([new DialogButton("common.ok", "save", DialogButtonKind.Primary)])["save"];
        public Button Save { get; }
        public Exception? CommitFailure { get; set; }
        public Exception? DiscardFailure { get; set; }
        public bool Unsaved { get; set; }
        protected override bool HasUnsavedChanges => Unsaved;

        protected override async Task<bool> TryCommitAsync(string tag)
        {
            await Task.Yield();
            if (CommitFailure is { } error)
                throw error;
            return true;
        }

        protected override async Task<bool> AskBeforeDiscardingAsync()
        {
            await Task.Yield();
            if (DiscardFailure is { } error)
                throw error;
            return true;
        }
    }
}
