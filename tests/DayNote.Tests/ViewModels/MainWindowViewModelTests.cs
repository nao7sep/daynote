using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using DayNote.Core.Configuration;
using DayNote.Core.Storage;
using DayNote.Logging;
using DayNote.Services;
using DayNote.ViewModels;
using DayNote.Views;
using DayNote.Tests.Storage;
using Xunit;

namespace DayNote.Tests.ViewModels;

/// <summary>
/// The main view model orchestrates open/close, autosave, dirty tracking, and the known-binders list —
/// the logic where data loss would hide. [AvaloniaFact] runs each test on the headless UI thread (which
/// owns the DispatcherTimers); the storage root is relocated to a throwaway directory via DAYNOTE_HOME.
/// Joined to the AppPaths collection so that process-wide env var never races another test.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _home;
    private readonly string? _previousHome;
    private readonly FakeDialogService _dialogs = new();

    public MainWindowViewModelTests()
    {
        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        _home = Path.Combine(Path.GetTempPath(), "daynote-vm-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _home);
    }

    private string BinderPath => Path.Combine(_home, "test.daynote");

    private MainWindowViewModel NewViewModel()
    {
        var vm = new MainWindowViewModel(new AppPaths(), _dialogs, new NullLogger());
        Assert.True(vm.IsReady);
        return vm;
    }

    private async Task<MainWindowViewModel> OpenNewBinderAsync()
    {
        var vm = NewViewModel();
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        Assert.True(vm.HasBinder);
        return vm;
    }

    [AvaloniaFact]
    public async Task New_binder_creates_the_file_and_lists_it()
    {
        var vm = await OpenNewBinderAsync();

        Assert.True(File.Exists(BinderPath));
        Assert.Single(vm.Binders);
        Assert.Equal("No notes", vm.BinderStatusText);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task New_note_selects_it_and_marks_unsaved()
    {
        var vm = await OpenNewBinderAsync();

        vm.NewNoteCommand.Execute(null);

        Assert.Single(vm.Notes);
        Assert.NotNull(vm.SelectedNote);
        Assert.Equal("Unsaved changes", vm.SaveStateText);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Saving_persists_notes_and_clears_the_dirty_state()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Title = "Persisted";
        vm.Editor.Body = "body text";

        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal("Saved", vm.SaveStateText);

        var reloaded = new BinderStore().Load(BinderPath);
        Assert.Single(reloaded.Binder.Notes);
        Assert.Equal("Persisted", reloaded.Binder.Notes[0].Title);
        Assert.Equal("body text", reloaded.Binder.Notes[0].Body);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Closing_a_binder_flushes_pending_edits_before_forgetting_it()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Body = "unsaved edit";

        // Close without an explicit save: closing must flush the dirty buffer first.
        await vm.CloseBinderCommand.ExecuteAsync(null);

        Assert.False(vm.HasBinder);
        Assert.Empty(vm.Binders);

        var reloaded = new BinderStore().Load(BinderPath);
        Assert.Equal("unsaved edit", reloaded.Binder.Notes[0].Body);
    }

    [AvaloniaFact]
    public async Task Binder_status_shows_the_note_count_only_when_no_note_is_selected()
    {
        var vm = await OpenNewBinderAsync();
        Assert.Equal("No notes", vm.BinderStatusText);

        vm.NewNoteCommand.Execute(null);
        Assert.Equal(string.Empty, vm.BinderStatusText);

        vm.NewNoteCommand.Execute(null);
        vm.SelectedNote = null;
        Assert.Equal("2 notes", vm.BinderStatusText);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Deleting_the_selected_note_removes_it_and_recovers_the_selection()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.NewNoteCommand.Execute(null);
        Assert.Equal(2, vm.Notes.Count);

        _dialogs.ConfirmResult = true;
        await vm.DeleteNoteCommand.ExecuteAsync(vm.SelectedNote);

        Assert.Single(vm.Notes);
        Assert.NotNull(vm.SelectedNote);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Deleting_a_note_is_a_no_op_when_the_confirmation_is_declined()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);

        _dialogs.ConfirmResult = false;
        await vm.DeleteNoteCommand.ExecuteAsync(vm.SelectedNote);

        Assert.Single(vm.Notes);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Removing_the_open_binder_closes_and_forgets_it()
    {
        var vm = await OpenNewBinderAsync();
        var row = Assert.Single(vm.Binders);

        await vm.RemoveBinderCommand.ExecuteAsync(row);

        Assert.Empty(vm.Binders);
        Assert.False(vm.HasBinder);
    }

    [AvaloniaFact]
    public async Task Adding_attachments_dedups_by_content_hash()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        var note = vm.SelectedNote!.Note;

        var sources = Path.Combine(_home, "sources");
        Directory.CreateDirectory(sources);
        var a = Path.Combine(sources, "a.txt");
        var aCopy = Path.Combine(sources, "a-copy.txt");
        var b = Path.Combine(sources, "b.txt");
        File.WriteAllText(a, "same content");
        File.WriteAllText(aCopy, "same content"); // identical bytes, different name
        File.WriteAllText(b, "different content");

        // Within one batch, the second identical file dedups against the first.
        vm.AddDroppedFiles(new[] { a, aCopy, b });
        Assert.Equal(2, note.Attachments.Count);

        // A later file whose content the note already holds is not copied again.
        var c = Path.Combine(sources, "c.txt");
        File.WriteAllText(c, "same content");
        vm.AddDroppedFiles(new[] { c });
        Assert.Equal(2, note.Attachments.Count);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Every_shortcut_action_routes_to_a_command_or_the_view()
    {
        // Guards against the old `default: return false` silently no-oping a newly-added
        // ShortcutAction: every action must route to a command (FilterNotes is view-handled).
        var vm = await OpenNewBinderAsync();
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            if (action == ShortcutAction.FilterNotes)
            {
                continue;
            }

            Assert.NotNull(ShortcutRouter.CommandFor(vm, action));
        }

        await vm.ShutdownAsync();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp directory is harmless.
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public string? BinderToCreate { get; set; }
        public string? BinderToOpen { get; set; }
        public IReadOnlyList<string> AttachmentPaths { get; set; } = Array.Empty<string>();
        public bool ConfirmResult { get; set; } = true;
        public ExternalChangeChoice ExternalChoice { get; set; } = ExternalChangeChoice.KeepMine;
        public bool SettingsApplied { get; set; }

        public Task<string?> PickBinderToOpenAsync() => Task.FromResult(BinderToOpen);
        public Task<string?> PickBinderToCreateAsync() => Task.FromResult(BinderToCreate);
        public Task<IReadOnlyList<string>> PickAttachmentsAsync() => Task.FromResult(AttachmentPaths);
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false) => Task.FromResult(ConfirmResult);
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowAboutAsync() => Task.CompletedTask;
        public Task ShowShortcutsAsync() => Task.CompletedTask;
        public Task<bool> ShowSettingsAsync(AppConfig config) => Task.FromResult(SettingsApplied);
        public Task<ExternalChangeChoice> AskExternalChangeAsync(string binderName) => Task.FromResult(ExternalChoice);
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
