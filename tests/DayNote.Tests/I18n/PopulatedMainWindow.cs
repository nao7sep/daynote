using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DayNote.Core.Backup;
using DayNote.Core.Configuration;
using DayNote.Core.Identity;
using DayNote.Core.Models;
using DayNote.Core.Storage;
using DayNote.I18n;
using DayNote.Logging;
using DayNote.Services;
using DayNote.ViewModels;
using DayNote.Views;

namespace DayNote.Tests.I18n;

/// <summary>
/// A main window with something on every surface that holds words: notes in each lifecycle state, the
/// selected one carrying every timestamp the status bar can show, an attachment on disk and one that
/// is gone, the attachments strip, an app-shell result and the text-style status. The language gates
/// read what it draws, so a surface that is empty in a test is one they cannot see.
///
/// The storage root is a throwaway directory set through <c>DAYNOTE_DATA_DIR</c>, which is process-wide,
/// so a test using this joins the <see cref="Storage.AppPathsEnvironment"/> collection and disposes it.
/// </summary>
internal sealed class PopulatedMainWindow : IDisposable
{
    private readonly string? _previousHome;
    private readonly HashSet<AttachmentItemViewModel> _attachmentRows = [];

    private PopulatedMainWindow(IAppLogger? log = null)
    {
        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        Home = Path.Combine(Path.GetTempPath(), "daynote-i18n-tests-" + IdGenerator.New());
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, Home);
        Dialogs = new QuietDialogs();
        ViewModel = new MainWindowViewModel(new AppPaths(), Dialogs, log ?? new NullLogger());
        ViewModel.Attachments.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is { } rows)
                foreach (AttachmentItemViewModel row in rows) _attachmentRows.Add(row);
        };
        Window = new MainWindow { DataContext = ViewModel };
        Dialogs.Owner = Window;
    }

    internal string Home { get; }

    internal QuietDialogs Dialogs { get; }

    internal MainWindowViewModel ViewModel { get; }

    internal MainWindow Window { get; }

    /// <summary>A main window over an empty storage root: no binder, no notes, every pane empty.</summary>
    internal static PopulatedMainWindow Empty(IAppLogger? log = null) => new(log);

    /// <summary>
    /// A main window with every surface populated. Call it on the UI thread, and show
    /// <see cref="Window"/> before <see cref="FillAsync"/>, as the app does.
    /// </summary>
    internal static PopulatedMainWindow Create(IAppLogger? log = null) => new(log);

    /// <summary>Opens a binder and puts something on every surface that holds words.</summary>
    internal async Task FillAsync()
    {
        var vm = ViewModel;
        Dialogs.BinderToCreate = Path.Combine(Home, "journal.daynote");
        await vm.NewBinderCommand.ExecuteAsync(null);

        // One note in each state, newest last so it ends up selected.
        foreach (var status in new[] { NoteStatus.Draft, NoteStatus.Discarded, NoteStatus.Verified, NoteStatus.Published })
        {
            vm.NewNoteCommand.Execute(null);
            vm.Editor.Title = "Note " + status;
            vm.Editor.Status = status;
        }

        // The selected note: attachments first, then retired, so the status bar carries its widest line
        // (verified, published and retired times together).
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Title = "A note with everything";
        vm.Editor.Body = "Some words to count.";
        var kept = Path.Combine(Home, "kept.txt");
        var gone = Path.Combine(Home, "gone.txt");
        File.WriteAllText(kept, "kept");
        File.WriteAllText(gone, "gone");
        await vm.AddDroppedFiles([kept, gone]);
        File.Delete(vm.Attachments.Single(item => item.FileName == "gone.txt").FullPath);

        // Reselect, so the row for the attachment that is gone says so, then fill the pane's strip.
        var selected = vm.SelectedNote;
        vm.SelectedNote = vm.Notes.First(note => !ReferenceEquals(note, selected));
        vm.SelectedNote = selected;
        await vm.AddDroppedFiles([kept], unavailable: 2);
        foreach (var status in new[] { NoteStatus.Verified, NoteStatus.Published, NoteStatus.Retired })
            vm.Editor.Status = status;

        // An app-shell result, and the text style just applied.
        Dialogs.NewBinderPickerError = new IOException("picker");
        await vm.NewBinderCommand.ExecuteAsync(null);
        Dialogs.NewBinderPickerError = null;
        await vm.CycleTextStyleCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        await Task.WhenAll(vm.Attachments.Select(row => row.ThumbnailTask));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Every surface that holds a user's value, filled with long ones: a long binder title in a deep
    /// folder, a long note title, a long untitled preview, several long attachment names, a message
    /// naming several files and a long text style. The text-fit rules are read against this.
    /// </summary>
    internal async Task FillLongAsync()
    {
        var vm = ViewModel;
        var deep = Path.Combine(Home, "an", "unusually", "deep", "folder", "structure", "for", "binders", "kept", "over", "years");
        Directory.CreateDirectory(deep);
        // A second binder in the same deep folder, which stays listed but not open.
        Dialogs.BinderToCreate = Path.Combine(deep, "another-binder-kept-beside-it-in-the-same-folder.daynote");
        await vm.NewBinderCommand.ExecuteAsync(null);
        Dialogs.BinderToCreate = Path.Combine(deep, "a-binder-file-with-a-rather-long-descriptive-name-for-testing.daynote");
        await vm.NewBinderCommand.ExecuteAsync(null);
        var row = vm.Binders.Single(binder => binder.IsCurrent);
        await vm.ApplyBinderRename(row, "A binder title long enough to need more room than the pane can give it");

        vm.NewNoteCommand.Execute(null);
        vm.Editor.Body = "An untitled note whose body runs on well past what the notes list can show on one line of text.";
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Title = "A note title that is far longer than any notes pane is wide, so it has to give way somehow";
        vm.Editor.Body = "Some words to count.";

        var files = new List<string>();
        foreach (var stem in new[] { "quarterly-report-for-the-regional-office-final-revised", "photograph-taken-at-the-harbour-at-dawn", "scanned-receipt-from-the-hardware-store", "meeting-minutes-with-every-attendee-listed" })
        {
            var file = Path.Combine(Home, stem + ".pdf");
            File.WriteAllText(file, stem);
            files.Add(file);
        }

        await vm.AddDroppedFiles(files);
        // Adding them again names several and counts the rest.
        await vm.AddDroppedFiles(files);

        await vm.CycleTextStyleCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        await Task.WhenAll(vm.Attachments.Select(row => row.ThumbnailTask));
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        // Quit through the app's own path before Home is deleted. Shutdown writes state.json under
        // Home, and a shown window left open is closed later by the headless platform, whose quit
        // runs shutdown again and recreates the deleted folder. Closing the window here runs that
        // quit now; a window never shown has no quit path, so its view model is shut down directly.
        // The save's continuation runs on the UI thread, so the thread is pumped rather than blocked.
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            if (Window.IsVisible)
            {
                var closed = false;
                Window.Closed += (_, _) => closed = true;
                Window.Close();
                while (!closed && DateTime.UtcNow < deadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(1);
                }
                if (!closed)
                {
                    throw new TimeoutException("The fixture window did not finish closing.");
                }
            }
            else
            {
                var shutdown = ViewModel.ShutdownAsync();
                while (!shutdown.IsCompleted && DateTime.UtcNow < deadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(1);
                }
                if (!shutdown.IsCompleted)
                {
                    throw new TimeoutException("The fixture view model did not finish shutting down.");
                }
                if (!shutdown.GetAwaiter().GetResult())
                {
                    throw new InvalidOperationException("The fixture view model could not save before shutdown.");
                }
            }
            var previews = Task.WhenAll(_attachmentRows.Select(row => row.ThumbnailTask));
            while (!previews.IsCompleted && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(1);
            }
            if (!previews.IsCompleted)
                throw new TimeoutException("The fixture attachment work did not finish.");
            previews.GetAwaiter().GetResult();
            var stateWrites = ViewModel.PendingStateWrites;
            while (!stateWrites.IsCompleted && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(1);
            }
            if (!stateWrites.IsCompleted)
                throw new TimeoutException("The fixture state writes did not finish.");
            stateWrites.GetAwaiter().GetResult();
        }
        finally
        {
            // Even a failed shutdown must release process-wide state before the next fixture.
            BackupStore.Close();
            Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        }
        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp directory is harmless.
        }
    }

    /// <summary>A dialog service that asks nothing: every confirmation is accepted, every error shown nowhere.</summary>
    internal sealed class QuietDialogs : IDialogService
    {
        public string? BinderToCreate { get; set; }
        public Exception? NewBinderPickerError { get; set; }
        public Avalonia.Controls.Window? Owner { get; set; }

        public Task<string?> PickBinderToOpenAsync(string? directory) => Task.FromResult<string?>(null);
        public Task<string?> PickBinderToCreateAsync(string? directory) => NewBinderPickerError is null
            ? Task.FromResult(BinderToCreate)
            : Task.FromException<string?>(NewBinderPickerError);
        public Task<IReadOnlyList<string>> PickAttachmentsAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> ConfirmAsync(Message title, Message message, string confirmLabelKey, bool destructive = false) => Task.FromResult(true);
        public Task ShowErrorAsync(Message title, Message message) => Task.CompletedTask;
        public Task ShowAboutAsync() => Task.CompletedTask;
        public Task ShowShortcutsAsync() => Task.CompletedTask;
        /// <summary>What the reader changes in Settings before pressing Save; nothing is saved when null.</summary>
        public Action<AppConfig>? SettingsEdit { get; set; }

        public Task<bool> ShowSettingsAsync(AppConfig config, Func<AppConfig, Task<bool>> trySave)
        {
            if (SettingsEdit is null)
                return Task.FromResult(false);
            SettingsEdit(config);
            return trySave(config);
        }
        public Task<ExternalChangeChoice> AskExternalChangeAsync(string binderName) => Task.FromResult(ExternalChangeChoice.KeepMine);
        public Task<UnsavedQuitChoice> AskQuitWithUnsavedBinderAsync(string binderName) => Task.FromResult(UnsavedQuitChoice.Stay);
        public async Task<bool> WaitForAttachmentsAsync(Task settled, TimeSpan stopOfferedAfter)
        {
            await settled;
            return false;
        }
        public string? LastOpenedPath { get; private set; }

        public Task OpenPathExternallyAsync(string path)
        {
            LastOpenedPath = path;
            return Task.CompletedTask;
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Debug(string message, object? data = null, Exception? error = null) { }
        public void Info(string message, object? data = null, Exception? error = null) { }
        public void Warn(string message, object? data = null, Exception? error = null) { }
        public void Error(string message, object? data = null, Exception? error = null) { }
    }
}
