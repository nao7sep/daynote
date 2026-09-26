using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
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
/// The storage root is a throwaway directory set through <c>DAYNOTE_HOME</c>, which is process-wide,
/// so a test using this joins the <see cref="Storage.AppPathsEnvironment"/> collection and disposes it.
/// </summary>
internal sealed class PopulatedMainWindow : IDisposable
{
    private readonly string? _previousHome;

    private PopulatedMainWindow()
    {
        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        Home = Path.Combine(Path.GetTempPath(), "daynote-i18n-tests-" + IdGenerator.New());
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, Home);
        Dialogs = new QuietDialogs();
        ViewModel = new MainWindowViewModel(new AppPaths(), Dialogs, new NullLogger());
        Window = new MainWindow { DataContext = ViewModel };
        Dialogs.Owner = Window;
    }

    internal string Home { get; }

    internal QuietDialogs Dialogs { get; }

    internal MainWindowViewModel ViewModel { get; }

    internal MainWindow Window { get; }

    /// <summary>A main window over an empty storage root: no binder, no notes, every pane empty.</summary>
    internal static PopulatedMainWindow Empty() => new();

    /// <summary>
    /// A main window with every surface populated. Call it on the UI thread, and show
    /// <see cref="Window"/> before <see cref="FillAsync"/>, as the app does.
    /// </summary>
    internal static PopulatedMainWindow Create() => new();

    /// <summary>Opens a binder and puts something on every surface that holds words.</summary>
    internal async Task FillAsync()
    {
        var vm = ViewModel;
        Dialogs.BinderToCreate = Path.Combine(Home, "journal.daynote");
        await vm.NewBinderCommand.ExecuteAsync(null);

        // One note in each state, newest last so it ends up selected.
        foreach (var status in new[] { NoteStatus.Draft, NoteStatus.Ready, NoteStatus.Published })
        {
            vm.NewNoteCommand.Execute(null);
            vm.Editor.Title = "Note " + status;
            vm.Editor.Status = status;
        }

        // The selected note: attachments first, while it can still be edited, then every status in
        // turn, so the status bar carries its widest line.
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
        foreach (var status in new[] { NoteStatus.Ready, NoteStatus.Published, NoteStatus.Expired })
            vm.Editor.Status = status;

        // An app-shell result, and the text style just applied.
        Dialogs.NewBinderPickerError = new IOException("picker");
        await vm.NewBinderCommand.ExecuteAsync(null);
        Dialogs.NewBinderPickerError = null;
        vm.CycleTextStyleCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        _ = ViewModel.ShutdownAsync();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
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

        public Task<string?> PickBinderToOpenAsync() => Task.FromResult<string?>(null);
        public Task<string?> PickBinderToCreateAsync() => NewBinderPickerError is null
            ? Task.FromResult(BinderToCreate)
            : Task.FromException<string?>(NewBinderPickerError);
        public Task<IReadOnlyList<string>> PickAttachmentsAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<bool> ConfirmAsync(Message title, Message message, string confirmLabelKey, bool destructive = false) => Task.FromResult(true);
        public Task ShowErrorAsync(Message title, Message message) => Task.CompletedTask;
        public Task ShowAboutAsync() => Task.CompletedTask;
        public Task ShowShortcutsAsync() => Task.CompletedTask;
        /// <summary>What the reader changes in Settings before pressing Save; nothing is saved when null.</summary>
        public Action<AppConfig>? SettingsEdit { get; set; }

        public Task<bool> ShowSettingsAsync(AppConfig config, Func<AppConfig, bool> trySave)
        {
            if (SettingsEdit is null)
                return Task.FromResult(false);
            SettingsEdit(config);
            return Task.FromResult(trySave(config));
        }
        public Task<ExternalChangeChoice> AskExternalChangeAsync(string binderName) => Task.FromResult(ExternalChangeChoice.KeepMine);
        public Task OpenPathExternallyAsync(string path) => Task.CompletedTask;
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Debug(string message, object? data = null, Exception? error = null) { }
        public void Info(string message, object? data = null, Exception? error = null) { }
        public void Warn(string message, object? data = null, Exception? error = null) { }
        public void Error(string message, object? data = null, Exception? error = null) { }
    }
}
