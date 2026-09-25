using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using DayNote.Core.Backup;
using DayNote.Core.Configuration;
using DayNote.Core.Identity;
using DayNote.Core.Models;
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
        _home = Path.Combine(Path.GetTempPath(), "daynote-vm-tests-" + IdGenerator.New());
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _home);
    }

    private string BinderPath => Path.Combine(_home, "test.daynote");

    private MainWindowViewModel NewViewModel(Action<string>? deleteFile = null, Action<string>? deleteDirectory = null)
    {
        var vm = new MainWindowViewModel(new AppPaths(), _dialogs, new NullLogger(), deleteFile, deleteDirectory);
        Assert.True(vm.IsReady);
        return vm;
    }

    [AvaloniaFact]
    public async Task Attachment_remove_failure_keeps_the_item_and_authors_hostile_diagnostics()
    {
        var hostile = new IOException("EACCES IPC /private/tmp/DAYNOTE-REMOVE-SENTINEL");
        var vm = NewViewModel(_ => throw hostile);
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        var source = Path.Combine(_home, "remove-me.txt");
        File.WriteAllText(source, "attachment content");
        await vm.AddDroppedFiles(new[] { source });
        var item = Assert.Single(vm.Attachments);

        await vm.RemoveAttachmentCommand.ExecuteAsync(item);

        Assert.Contains(item.FileName, vm.SelectedNote!.Note.Attachments);
        Assert.True(File.Exists(item.FullPath));
        var result = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Contains("remains attached", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("DAYNOTE-REMOVE-SENTINEL", result.Message, StringComparison.Ordinal);
        Assert.Empty(vm.Results);
        await vm.ShutdownAsync();
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
    public async Task Mandatory_panes_explain_prerequisites_and_ordinary_empty_states()
    {
        var vm = NewViewModel();
        Assert.Equal("No binders yet. Create or open one.", vm.BindersEmptyStateText);
        Assert.Equal("Open or create a binder to add notes.", vm.NotesEmptyStateText);
        Assert.Equal("Select or create a note to add attachments.", vm.AttachmentsEmptyStateText);

        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, vm.BindersEmptyStateText);
        Assert.Equal("No notes yet. Create one to get started.", vm.NotesEmptyStateText);

        vm.NewNoteCommand.Execute(null);
        Assert.Equal(string.Empty, vm.NotesEmptyStateText);
        Assert.Equal("No attachments yet.", vm.AttachmentsEmptyStateText);

        await vm.CloseBinderCommand.ExecuteAsync(null);
        Assert.Equal("No binders yet. Create or open one.", vm.BindersEmptyStateText);
        Assert.Equal("Open or create a binder to add notes.", vm.NotesEmptyStateText);
        Assert.Equal("Select or create a note to add attachments.", vm.AttachmentsEmptyStateText);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Native_picker_failures_remain_owned_by_the_initiating_surface()
    {
        var hostile = new IOException("EACCES IPC /private/tmp/DAYNOTE-PICKER-SENTINEL");
        var vm = NewViewModel();

        _dialogs.NewBinderPickerError = hostile;
        await vm.NewBinderCommand.ExecuteAsync(null);
        var newBinder = Assert.Single(vm.Results);
        Assert.Contains("new-binder picker", newBinder.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("EACCES", newBinder.Message, StringComparison.Ordinal);

        _dialogs.NewBinderPickerError = null;
        _dialogs.OpenBinderPickerError = hostile;
        await vm.OpenBinderCommand.ExecuteAsync(null);
        Assert.Contains(vm.Results, result => result.Message.Contains("binder picker", StringComparison.Ordinal));

        _dialogs.OpenBinderPickerError = null;
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        _dialogs.AttachmentPickerError = hostile;
        await vm.AddAttachmentCommand.ExecuteAsync(null);

        Assert.NotNull(vm.AttachmentResult);
        Assert.Contains("attachment picker", vm.AttachmentResult!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("DAYNOTE-PICKER-SENTINEL", vm.AttachmentResult.Message, StringComparison.Ordinal);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Failed_external_reload_never_publishes_success()
    {
        var vm = await OpenNewBinderAsync();
        File.WriteAllText(BinderPath, "not valid DayNote data");

        await vm.CheckExternalChangeAsync();

        var result = Assert.Single(
            vm.Results,
            item => item.Message.Contains("changed on disk", StringComparison.Ordinal));
        Assert.Equal(OperationResultKind.Error, result.Kind);
        Assert.DoesNotContain(
            vm.Results,
            item => item.Message.Contains("Reloaded after", StringComparison.Ordinal));
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public void First_run_creates_config_json_but_not_state_json()
    {
        var configFile = Path.Combine(_home, "config.json");
        var stateFile = Path.Combine(_home, "state.json");
        Assert.False(File.Exists(configFile));

        _ = NewViewModel();

        // config.json is written on first run so the settings file is present and hand-editable
        // immediately; state.json (volatile UI state) is deliberately not created until there is state.
        Assert.True(File.Exists(configFile));
        Assert.False(File.Exists(stateFile));

        // A second launch is create-if-absent, so the existing file is left byte-for-byte untouched.
        var after = File.ReadAllText(configFile);
        _ = NewViewModel();
        Assert.Equal(after, File.ReadAllText(configFile));
    }

    [AvaloniaFact]
    public void First_run_materializes_config_and_the_write_through_store_records_it()
    {
        // The write-through backup records config.json the instant its atomic write lands. The
        // constructor materializes config.json synchronously (LoadConfigAndState via CreateIfMissing,
        // which goes through AtomicFile), so the very first launch must already hold a config.json row —
        // the regression this guards is materialization drifting away from the atomic-write choke point,
        // which would leave the store with no record of the file it just created.
        _ = NewViewModel();

        var paths = new AppPaths();
        BackupStore.Close(); // release the file handle the constructor opened, so we can read it here

        var configFile = Path.Combine(_home, "config.json");
        Assert.Equal(1, RowCountFor(paths.BackupStoreFile, configFile));
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
    public async Task Overlapping_saves_of_the_same_binder_never_lose_the_latest_edit()
    {
        // The binder write now runs on a background thread (DN-1); a manual Save landing while the
        // autosave timer's own save is still writing must queue behind it rather than starting a second,
        // overlapping write to the same file. Simulate that by starting one save, dirtying the note
        // again before it can possibly have finished its background I/O, and starting a second save
        // without awaiting the first — exactly what a stray extra Ctrl+S or a timer tick can do.
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Title = "First";
        vm.Editor.Body = "first body";

        var firstSave = vm.SaveNowCommand.ExecuteAsync(null);
        vm.Editor.Body = "second body";
        var secondSave = vm.SaveNowCommand.ExecuteAsync(null);

        await Task.WhenAll(firstSave, secondSave);

        Assert.Equal("Saved", vm.SaveStateText);
        Assert.Empty(vm.Results);
        var reloaded = new BinderStore().Load(BinderPath);
        Assert.Equal("second body", reloaded.Binder.Notes[0].Body);

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
        await vm.AddDroppedFiles(new[] { a, aCopy, b });
        Assert.Equal(2, note.Attachments.Count);
        var firstResult = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Equal(OperationResultKind.Info, firstResult.Kind);
        Assert.True(firstResult.IsPersistent);
        Assert.Contains("Already attached", firstResult.Message);
        Assert.Empty(vm.Results);

        // A later file whose content the note already holds is not copied again.
        var c = Path.Combine(sources, "c.txt");
        File.WriteAllText(c, "same content");
        await vm.AddDroppedFiles(new[] { c });
        Assert.Equal(2, note.Attachments.Count);
        var replacement = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.True(replacement.IsPersistent);
        Assert.NotSame(firstResult, replacement);

        vm.DismissAttachmentResult();
        Assert.Null(vm.AttachmentResult);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Attachment_results_do_not_escape_to_the_shell_result_host()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);

        var source = Path.Combine(_home, "source.txt");
        File.WriteAllText(source, "attachment content");
        var assetsDirectory = BinderStore.AssetsDirectory(BinderPath);
        File.WriteAllText(assetsDirectory, "blocked");

        await vm.AddDroppedFiles(new[] { source });

        var error = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Equal(OperationResultKind.Error, error.Kind);
        Assert.True(error.IsPersistent);
        Assert.Equal(error.Message, error.AccessibleMessage);
        Assert.Equal(AutomationLiveSetting.Assertive, error.LiveSetting);

        File.Delete(assetsDirectory);
        await vm.AddDroppedFiles(new[] { source });
        await vm.AddDroppedFiles(new[] { source });

        var information = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Equal(OperationResultKind.Info, information.Kind);
        Assert.True(information.IsPersistent);
        Assert.Equal(information.Message, information.AccessibleMessage);
        Assert.Equal(AutomationLiveSetting.Polite, information.LiveSetting);
        Assert.Empty(vm.Results);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Repeated_save_failure_coalesces_and_recovery_resolves_only_that_result()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        var note = vm.SelectedNote!.Note;

        // Establish a pane-owned attachment result alongside the independent shell save result.
        await vm.AddDroppedFiles([], unavailable: 1);

        var source = Path.Combine(_home, "source.txt");
        File.WriteAllText(source, "attachment content");
        var noteAssets = BinderStore.NoteAssetsDirectory(BinderPath, note.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(noteAssets)!);
        File.WriteAllText(noteAssets, "blocks the attachment directory");
        await vm.AddDroppedFiles(new[] { source });

        File.Delete(noteAssets);
        await vm.AddDroppedFiles(new[] { source });
        await vm.AddDroppedFiles(new[] { source });
        Assert.NotNull(vm.AttachmentResult);
        Assert.Empty(vm.Results);

        // A directory at the binder-file path makes the real atomic save fail on every retry.
        File.Delete(BinderPath);
        Directory.CreateDirectory(BinderPath);
        vm.Editor.Title = "Unsaved title";

        await vm.SaveNowCommand.ExecuteAsync(null);
        var firstFailure = Assert.Single(vm.Results, result => result.ResultKey is not null);
        Assert.Equal(OperationResultKind.Error, firstFailure.Kind);
        Assert.StartsWith("Your changes are still in DayNote", firstFailure.Message);

        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Single(vm.Results, result => result.ResultKey == firstFailure.ResultKey);

        Directory.Delete(BinderPath);
        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
        Assert.NotNull(vm.AttachmentResult);
        Assert.Equal("Saved", vm.SaveStateText);
        Assert.True(File.Exists(BinderPath));

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Adding_an_attachment_when_the_assets_directory_cannot_be_created_is_recoverable()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        var note = vm.SelectedNote!.Note;

        var source = Path.Combine(_home, "source.txt");
        File.WriteAllText(source, "attachment content");
        // Occupy the assets-directory path with a file so Directory.CreateDirectory must fail.
        File.WriteAllText(BinderStore.AssetsDirectory(BinderPath), "blocked");

        var exception = await Record.ExceptionAsync(() => vm.AddDroppedFiles(new[] { source }));

        Assert.Null(exception);
        Assert.Empty(note.Attachments);
        var result = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Contains("Could not prepare", result.Message);
        Assert.Empty(vm.Results);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Unavailable_drop_items_are_accounted_for_without_mutating_attachments()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);

        await vm.AddDroppedFiles([], unavailable: 2);

        Assert.Empty(vm.Attachments);
        var result = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Equal(OperationResultKind.Warning, result.Kind);
        Assert.True(result.IsPersistent);
        Assert.Equal("2 items are not readable local files.", result.Message);
        Assert.Empty(vm.Results);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Attachment_open_failure_stays_on_the_row_and_successful_retry_clears_it()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);

        var source = Path.Combine(_home, "open-me.txt");
        File.WriteAllText(source, "attachment content");
        await vm.AddDroppedFiles(new[] { source });
        var item = Assert.Single(vm.Attachments);

        _dialogs.OpenPathError = new IOException("test open failure");
        await vm.OpenAttachmentCommand.ExecuteAsync(item);

        var failure = Assert.IsType<OperationResultViewModel>(item.Result);
        Assert.Equal(OperationResultKind.Error, failure.Kind);
        Assert.Equal(AutomationLiveSetting.Assertive, failure.LiveSetting);
        Assert.Contains("Double-click to try again", failure.Message);
        Assert.Null(vm.AttachmentResult);
        Assert.Empty(vm.Results);

        var anotherSource = Path.Combine(_home, "another.txt");
        File.WriteAllText(anotherSource, "different attachment content");
        await vm.AddDroppedFiles(new[] { anotherSource });

        Assert.Contains(item, vm.Attachments);
        Assert.Same(failure, item.Result);

        _dialogs.OpenPathError = null;
        await vm.OpenAttachmentCommand.ExecuteAsync(item);

        Assert.Null(item.Result);
        Assert.Equal(item.FullPath, _dialogs.LastOpenedPath);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Missing_attachment_is_reported_on_its_row()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);

        var source = Path.Combine(_home, "goes-missing.txt");
        File.WriteAllText(source, "attachment content");
        await vm.AddDroppedFiles(new[] { source });
        var selectedNote = vm.SelectedNote;
        var originalItem = Assert.Single(vm.Attachments);
        File.Delete(originalItem.FullPath);
        vm.SelectedNote = null;
        vm.SelectedNote = selectedNote;

        var item = Assert.Single(vm.Attachments);
        var result = Assert.IsType<OperationResultViewModel>(item.Result);
        Assert.Equal(OperationResultKind.Warning, result.Kind);
        Assert.Contains("unavailable on disk", result.Message);
        Assert.Equal("Unavailable", item.DetailsText);
        Assert.Null(vm.AttachmentResult);
        Assert.Empty(vm.Results);

        await vm.OpenAttachmentCommand.ExecuteAsync(item);
        Assert.Same(result, item.Result);

        File.WriteAllText(item.FullPath, "restored attachment content");
        await vm.OpenAttachmentCommand.ExecuteAsync(item);

        Assert.True(item.Exists);
        Assert.Null(item.Result);
        Assert.NotEqual("Unavailable", item.DetailsText);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Cancelling_a_live_attachment_reorder_restores_stable_items_and_the_durable_order()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        var note = vm.SelectedNote!.Note;
        await AddThreeAttachmentsAsync(vm);
        var startingItems = vm.Attachments.ToArray();
        var startingNames = note.Attachments.ToArray();

        Assert.True(vm.MoveAttachment(startingItems[0], 2));
        Assert.NotEqual(startingNames, vm.Attachments.Select(item => item.FileName));
        Assert.Equal(startingNames, note.Attachments); // preview has not committed

        Assert.True(vm.RestoreAttachmentOrder(startingItems));
        Assert.Equal(startingItems, vm.Attachments);
        Assert.Equal(startingNames, note.Attachments);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Attachment_list_registers_as_the_native_reorder_receiver()
    {
        var (vm, window, list) = await OpenWindowWithThreeAttachmentsAsync();

        Assert.True(DragDrop.GetAllowDrop(list));

        await CloseTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task Attachment_pane_routes_external_files_and_neighboring_dead_space_denies()
    {
        var (vm, window, _) = await OpenWindowWithThreeAttachmentsAsync();
        var pane = Assert.IsType<Border>(window.FindControl<Border>("AttachPane"));
        var toolbar = Assert.IsType<Border>(window.GetVisualDescendants()
            .OfType<Border>()
            .First(control => control.GetValue(Grid.RowProperty) == 0));
        var source = Path.Combine(_home, "headless-drop.txt");
        File.WriteAllText(source, "delivered");
        var storageFile = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(source));
        Assert.NotNull(storageFile);
        using var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateFile(storageFile));
        Assert.True(vm.Editor.HasNote);
        Assert.True(((IDataTransfer)transfer).Contains(DataFormat.File));
        var panePoint = pane.TranslatePoint(new Point(10, 10), window);
        var toolbarPoint = toolbar.TranslatePoint(new Point(10, 10), window);
        Assert.NotNull(panePoint);
        Assert.NotNull(toolbarPoint);

        window.DragDrop(panePoint.Value, RawDragEventType.DragEnter, transfer, DragDropEffects.Copy, RawInputModifiers.None);
        window.DragDrop(panePoint.Value, RawDragEventType.DragOver, transfer, DragDropEffects.Copy, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsAttachmentDropActive);

        // The drop handler now hashes and copies the file on a background thread (DN-3), so the
        // attachment lands asynchronously; pump the dispatcher until the background add's UI-thread
        // continuation has run rather than asserting immediately after one synchronous RunJobs.
        window.DragDrop(panePoint.Value, RawDragEventType.Drop, transfer, DragDropEffects.Copy, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsAttachmentDropActive);
        await PumpUntilAsync(() => vm.Attachments.Any(attachment => attachment.FileName == "headless-drop.txt"));

        var count = vm.Attachments.Count;
        window.DragDrop(toolbarPoint.Value, RawDragEventType.Drop, transfer, DragDropEffects.Copy, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(count, vm.Attachments.Count);

        await CloseTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task Keyboard_attachment_move_commits_once_and_follows_the_selected_item()
    {
        var (vm, window, list) = await OpenWindowWithThreeAttachmentsAsync();
        var note = vm.SelectedNote!.Note;
        var moved = vm.Attachments[1];
        list.SelectedItem = moved;
        Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(1)).Focus();
        var command = ShortcutCatalog.CommandModifier(window) == KeyModifiers.Meta
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;

        window.KeyPress(Key.Up, command | RawInputModifiers.Shift, PhysicalKey.ArrowUp, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(moved, vm.Attachments[0]);
        Assert.Same(moved, list.SelectedItem);
        Assert.True(list.IsKeyboardFocusWithin);
        Assert.Equal(vm.Attachments.Select(item => item.FileName), note.Attachments);
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(note.Attachments, new BinderStore().Load(BinderPath).Binder.Notes.Single().Attachments);

        await CloseTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task Cycling_text_styles_moves_the_default_flag_and_names_the_family()
    {
        var vm = NewViewModel();
        var configPath = Path.Combine(_home, "config.json");

        vm.CycleTextStyleCommand.Execute(null);

        Assert.Equal("Text style: Inter", vm.TextStyleStatusText);
        Assert.Empty(vm.Results);
        var saved = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath), DayNoteJson.Options)!;
        Assert.Equal(new[] { false, true }, saved.TextStyles.Select(style => style.IsDefault));

        vm.CycleTextStyleCommand.Execute(null);

        Assert.Equal("Text style: Menlo", vm.TextStyleStatusText);
        Assert.Empty(vm.Results);
        saved = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(configPath), DayNoteJson.Options)!;
        Assert.Equal(new[] { true, false }, saved.TextStyles.Select(style => style.IsDefault));

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Keyboard_binder_move_passes_hidden_rows_and_persists_the_order_once()
    {
        var vm = NewViewModel();
        foreach (var name in new[] { "alpha", "hidden", "alpine" })
        {
            _dialogs.BinderToCreate = Path.Combine(_home, name + ".daynote");
            await vm.NewBinderCommand.ExecuteAsync(null);
        }

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("BindersList"));
        Assert.True(DragDrop.GetAllowDrop(list));
        vm.BindersFilter = "a";
        Dispatcher.UIThread.RunJobs();
        var moved = Assert.Single(vm.Binders, binder => binder.Name == "alpine");
        Assert.Equal(new[] { "alpine", "alpha" }, vm.Binders.Select(binder => binder.Name));
        Assert.Same(moved, list.SelectedItem);
        Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0)).Focus();
        var command = ShortcutCatalog.CommandModifier(window) == KeyModifiers.Meta
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;

        window.KeyPress(Key.Down, command | RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "alpha", "alpine" }, vm.Binders.Select(binder => binder.Name));
        Assert.Same(moved, list.SelectedItem);
        Assert.True(list.IsKeyboardFocusWithin);
        var saved = JsonSerializer.Deserialize<AppState>(
            File.ReadAllText(Path.Combine(_home, "state.json")), DayNoteJson.Options)!;
        Assert.Equal(
            new[] { "hidden.daynote", "alpha.daynote", "alpine.daynote" },
            saved.Binders.Select(entry => Path.GetFileName(entry.Path)));

        await CloseTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task Cancelling_a_binder_drag_restores_the_master_order_without_persisting()
    {
        var vm = NewViewModel();
        foreach (var name in new[] { "one", "two", "three" })
        {
            _dialogs.BinderToCreate = Path.Combine(_home, name + ".daynote");
            await vm.NewBinderCommand.ExecuteAsync(null);
        }

        var statePath = Path.Combine(_home, "state.json");
        var before = File.ReadAllText(statePath);
        var start = vm.BinderOrder();

        Assert.True(vm.MoveBinder(start[0], start[2]));
        Assert.Equal(new[] { start[1], start[2], start[0] }, vm.BinderOrder());
        Assert.True(vm.RestoreBinderOrder(start));
        vm.CommitBinderOrder();

        Assert.Equal(start, vm.BinderOrder());
        Assert.Equal(start, vm.Binders);
        Assert.Equal(before, File.ReadAllText(statePath));

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Shell_results_keep_one_card_per_subject_with_the_newest_on_top()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);

        // Someone else edits the file while this window holds no unsaved work.
        var store = new BinderStore();
        var outside = store.Load(BinderPath).Binder;
        outside.Notes[0].Title = "Changed outside";
        store.Save(BinderPath, outside);
        await vm.CheckExternalChangeAsync();

        var reloaded = Assert.Single(vm.Results);
        Assert.Equal(OperationResultKind.Info, reloaded.Kind);
        Assert.False(reloaded.IsPersistent);
        Assert.Same(reloaded, vm.AnnouncedResult);

        // Another subject goes on top and leaves the first card where it stands.
        _dialogs.OpenBinderPickerError = new IOException("picker unavailable");
        await vm.OpenBinderCommand.ExecuteAsync(null);
        var picker = vm.Results[0];
        Assert.Equal(2, vm.Results.Count);
        Assert.Same(reloaded, vm.Results[1]);
        Assert.Same(picker, vm.AnnouncedResult);

        // Repeating that failure is neither a second card nor a second announcement.
        await vm.OpenBinderCommand.ExecuteAsync(null);
        _dialogs.OpenBinderPickerError = null;
        Assert.Equal(2, vm.Results.Count);
        Assert.Same(picker, vm.Results[0]);

        // A later message replaces its subject's card in place, whatever its severity: the file is
        // one subject whether it was reloaded or is gone.
        File.Delete(BinderPath);
        await vm.CheckExternalChangeAsync();
        var warning = vm.Results[1];
        Assert.Equal(OperationResultKind.Warning, warning.Kind);
        Assert.True(warning.IsPersistent);

        // The same unresolved problem again is neither a second card nor a second announcement.
        await vm.CheckExternalChangeAsync();
        Assert.Same(warning, vm.Results[1]);
        Assert.Equal(2, vm.Results.Count);

        // A result that leaves stops being the announcement, so the same failure after a dismissal
        // is a new card and a new announcement.
        vm.DismissResult(warning);
        Assert.Null(vm.AnnouncedResult);
        vm.DismissResult(picker);
        _dialogs.OpenBinderPickerError = new IOException("picker unavailable");
        await vm.OpenBinderCommand.ExecuteAsync(null);
        _dialogs.OpenBinderPickerError = null;
        var again = Assert.Single(vm.Results);
        Assert.NotSame(picker, again);
        Assert.Same(again, vm.AnnouncedResult);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Deleting_a_note_takes_its_attachment_folder_with_it()
    {
        var vm = await OpenNewBinderAsync();
        var kept = Path.Combine(_home, "kept.txt");
        var doomed = Path.Combine(_home, "doomed.txt");
        File.WriteAllText(kept, "kept");
        File.WriteAllText(doomed, "doomed");

        vm.NewNoteCommand.Execute(null);
        var keeper = vm.SelectedNote!.Note;
        _dialogs.AttachmentPaths = [kept];
        await vm.AddAttachmentCommand.ExecuteAsync(null);

        vm.NewNoteCommand.Execute(null);
        var victim = vm.SelectedNote!.Note;
        _dialogs.AttachmentPaths = [doomed];
        await vm.AddAttachmentCommand.ExecuteAsync(null);
        await vm.SaveNowCommand.ExecuteAsync(null);

        var keeperAssets = BinderStore.NoteAssetsDirectory(BinderPath, keeper.Id);
        var victimAssets = BinderStore.NoteAssetsDirectory(BinderPath, victim.Id);
        Assert.True(Directory.Exists(keeperAssets));
        Assert.Single(Directory.GetFiles(victimAssets));

        await vm.DeleteNoteCommand.ExecuteAsync(null);

        // The note's own folder is named by its id: leaving it would leave bytes nobody can resolve.
        Assert.False(Directory.Exists(victimAssets));
        Assert.True(Directory.Exists(keeperAssets));
        Assert.Single(Directory.GetFiles(keeperAssets));
        Assert.Empty(vm.Results);
        // The file the user attached FROM is theirs and is untouched.
        Assert.True(File.Exists(doomed));

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_note_whose_attachments_could_not_be_deleted_says_so()
    {
        var vm = NewViewModel(deleteDirectory: _ => throw new IOException("in use"));
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        var file = Path.Combine(_home, "locked.txt");
        File.WriteAllText(file, "locked");
        vm.NewNoteCommand.Execute(null);
        var note = vm.SelectedNote!.Note;
        _dialogs.AttachmentPaths = [file];
        await vm.AddAttachmentCommand.ExecuteAsync(null);
        await vm.SaveNowCommand.ExecuteAsync(null);

        await vm.DeleteNoteCommand.ExecuteAsync(null);

        // The note is gone either way; the files that outlived it are the user's to know about.
        Assert.Empty(vm.Notes);
        var warning = Assert.Single(vm.Results);
        Assert.Equal(OperationResultKind.Warning, warning.Kind);
        Assert.Contains("attachment files", warning.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(BinderStore.NoteAssetsDirectory(BinderPath, note.Id)));

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_published_note_keeps_the_attachments_it_was_published_with()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        var first = Path.Combine(_home, "first.txt");
        var second = Path.Combine(_home, "second.txt");
        var late = Path.Combine(_home, "late.txt");
        // Distinct content: the note dedups attachments by content hash.
        foreach (var file in new[] { first, second, late })
        {
            File.WriteAllText(file, file);
        }

        _dialogs.AttachmentPaths = [first, second];
        await vm.AddAttachmentCommand.ExecuteAsync(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        var published = vm.Attachments.Select(attachment => attachment.FileName).ToArray();
        Assert.Equal(2, published.Length);

        vm.Editor.Status = NoteStatus.Published;
        Assert.False(vm.CanEditNote);

        // A published note's text is read-only, and so is the set of files it carries: nothing adds,
        // removes, or reorders them until it goes back to a draft.
        Assert.False(vm.MoveAttachment(vm.Attachments[1], 0));
        _dialogs.AttachmentPaths = [late];
        await vm.AddAttachmentCommand.ExecuteAsync(null);
        await vm.AddDroppedFiles([late]);
        await vm.RemoveAttachmentCommand.ExecuteAsync(vm.Attachments[0]);
        Assert.Equal(published, vm.Attachments.Select(attachment => attachment.FileName));

        // Back in a draft, all three work again.
        vm.Editor.Status = NoteStatus.Draft;
        Assert.True(vm.CanEditNote);
        Assert.True(vm.MoveAttachment(vm.Attachments[1], 0));
        await vm.AddDroppedFiles([late]);
        Assert.Equal(3, vm.Attachments.Count);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_binder_deleted_outside_the_app_shows_on_its_row_and_can_be_removed()
    {
        var vm = NewViewModel();
        var travel = Path.Combine(_home, "travel.daynote");
        _dialogs.BinderToCreate = travel;
        await vm.NewBinderCommand.ExecuteAsync(null);
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        var missing = vm.Binders.Single(binder => PathKey.Equal(binder.Path, travel));
        var open = vm.Binders.Single(binder => PathKey.Equal(binder.Path, BinderPath));

        // Deleting the file in Finder, with the binder never opened here.
        File.Delete(travel);
        vm.RefreshKnownBinders();
        Assert.True(missing.IsMissing);
        Assert.Empty(vm.Results);

        // The open binder's file is reported with its recovery instead, so the row stays quiet.
        File.Delete(BinderPath);
        vm.RefreshKnownBinders();
        Assert.False(open.IsMissing);

        // Removing the entry needs no file, and it does not come back.
        await vm.RemoveBinderCommand.ExecuteAsync(missing);
        Assert.DoesNotContain(vm.Binders, binder => PathKey.Equal(binder.Path, travel));
        var state = JsonSerializer.Deserialize<AppState>(
            File.ReadAllText(Path.Combine(_home, "state.json")), DayNoteJson.Options)!;
        Assert.DoesNotContain(state.Binders, entry => PathKey.Equal(entry.Path, travel));

        // A file that comes back clears the marker on the next look.
        File.WriteAllText(travel, "{}");
        vm.RefreshKnownBinders();
        Assert.DoesNotContain(vm.Binders, binder => binder.IsMissing);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_missing_binder_is_reported_on_its_own_row()
    {
        var vm = NewViewModel();
        var travel = Path.Combine(_home, "travel.daynote");
        _dialogs.BinderToCreate = travel;
        await vm.NewBinderCommand.ExecuteAsync(null);
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        File.Delete(travel);
        var missing = Assert.Single(vm.Binders, binder => PathKey.Equal(binder.Path, travel));
        Assert.False(missing.IsMissing);

        await vm.OpenKnownBinderCommand.ExecuteAsync(missing);

        // The row owns the condition: no shell card, and the open binder is untouched.
        Assert.True(missing.IsMissing);
        Assert.Empty(vm.Results);
        Assert.True(PathKey.Equal(vm.Binders.Single(binder => binder.IsCurrent).Path, BinderPath));

        // The marker states what is true now, so the file coming back clears it.
        File.WriteAllText(travel, File.ReadAllText(BinderPath));
        await vm.OpenKnownBinderCommand.ExecuteAsync(missing);
        Assert.False(missing.IsMissing);
        Assert.True(PathKey.Equal(vm.Binders.Single(binder => binder.IsCurrent).Path, travel));

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_save_or_close_resolves_what_the_binder_file_did_on_disk()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);

        File.Delete(BinderPath);
        await vm.CheckExternalChangeAsync();
        Assert.Equal(OperationResultKind.Warning, Assert.Single(vm.Results).Kind);

        // Saving recreates the file, which is exactly what the warning promised.
        vm.Editor.Title = "Recreated";
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.True(File.Exists(BinderPath));
        Assert.Empty(vm.Results);

        // Once the binder is closed, its file's fate no longer concerns the workspace.
        File.Delete(BinderPath);
        await vm.CheckExternalChangeAsync();
        Assert.Single(vm.Results);
        await vm.CloseBinderCommand.ExecuteAsync(null);
        Assert.False(vm.HasBinder);
        Assert.Empty(vm.Results);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Shell_results_float_over_the_panes_without_moving_them_or_reserving_window_space()
    {
        var (vm, window) = await OpenWindowWithNoteAsync();
        var panes = new[] { "BindersPane", "NotesPane", "EditorPane", "AttachPane" }
            .Select(name => Assert.IsType<Border>(window.FindControl<Border>(name)))
            .ToArray();
        var layout = Assert.IsType<Grid>(window.FindControl<Grid>("LayoutRoot"));
        var before = panes.Select(pane => pane.Bounds).ToArray();
        var minimums = (layout.MinWidth, layout.MinHeight, window.MinWidth, window.MinHeight);

        await ShowEveryShellResultAsync(vm, window);
        Assert.Equal(before, panes.Select(pane => pane.Bounds));
        Assert.Equal(minimums, (layout.MinWidth, layout.MinHeight, window.MinWidth, window.MinHeight));

        foreach (var close in ResultCards(window).Select(card => CloseButton(card)).ToArray())
        {
            close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(before, panes.Select(pane => pane.Bounds));
        }

        Assert.Empty(vm.Results);
        await CloseResultsTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task Every_result_the_policy_allows_is_whole_and_clear_of_the_headers_at_the_default_size()
    {
        var (vm, window) = await OpenWindowWithNoteAsync();
        await ShowEveryShellResultAsync(vm, window);
        var viewport = Assert.IsType<ScrollViewer>(window.FindControl<ScrollViewer>("ResultsViewport"));
        var cards = ResultCards(window);

        Assert.Equal(1200, window.ClientSize.Width);
        Assert.Equal(800, window.ClientSize.Height);
        Assert.Equal(vm.Results.Count, cards.Count);
        Assert.True(viewport.Extent.Height <= viewport.Viewport.Height, "The whole stack fits without scrolling.");
        var headers = new[] { "AttachmentsHeader", "EditorHeader" }
            .Select(name => BoundsIn(window, Assert.IsAssignableFrom<Control>(window.FindControl<Control>(name))))
            .ToArray();
        foreach (var card in cards)
        {
            var inViewport = BoundsIn(viewport, card);
            Assert.True(inViewport.Top >= 0 && inViewport.Bottom <= viewport.Bounds.Height, "A card is shown whole.");
            Assert.All(headers, header => Assert.False(BoundsIn(window, card).Intersects(header)));
        }

        // The stack lives in the window's own tree, not a popup window, so a modal dialog (an owned
        // window) always sits above it.
        Assert.Same(window, TopLevel.GetTopLevel(viewport));
        Assert.Empty(viewport.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.Popup>());

        await CloseResultsTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task An_overflowing_stack_scrolls_below_the_headers_and_reaches_every_card_whole()
    {
        var (vm, window) = await OpenWindowWithNoteAsync();
        window.Width = 900;
        // The smallest window the app allows: four results are taller than the room it leaves them.
        window.Height = window.MinHeight;
        await ShowEveryShellResultAsync(vm, window);
        var viewport = Assert.IsType<ScrollViewer>(window.FindControl<ScrollViewer>("ResultsViewport"));

        Assert.True(viewport.Extent.Height > viewport.Viewport.Height, "The results outgrow the smallest window.");
        var scrollBar = Assert.Single(
            viewport.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(),
            bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.True(scrollBar.IsEffectivelyVisible);
        Assert.False(viewport.AllowAutoHide);
        foreach (var name in new[] { "AttachmentsHeader", "EditorHeader" })
        {
            var header = BoundsIn(window, Assert.IsAssignableFrom<Control>(window.FindControl<Control>(name)));
            Assert.True(BoundsIn(window, viewport).Top >= header.Bottom - 0.5, $"The stack stays below {name}.");
        }

        foreach (var card in ResultCards(window))
        {
            card.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var inViewport = BoundsIn(viewport, card);
            Assert.True(
                inViewport.Top >= -0.5 && inViewport.Bottom <= viewport.Bounds.Height + 0.5,
                "Scrolling brings every card fully into view.");
        }

        await CloseResultsTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task A_one_line_result_is_balanced_and_the_close_mark_sits_on_the_first_line()
    {
        var (vm, window) = await OpenWindowWithNoteAsync();
        await ShowEveryShellResultAsync(vm, window);
        var cards = ResultCards(window);
        var oneLine = cards.Single(card => card.DataContext == vm.Results.Single(r => r.Kind == OperationResultKind.Info));
        var wrapped = cards.Single(card => card.DataContext is OperationResultViewModel { Message: var m }
            && m.StartsWith("Your changes are still in DayNote", StringComparison.Ordinal));

        foreach (var card in new[] { oneLine, wrapped })
        {
            var text = card.GetVisualDescendants().OfType<TextBlock>().First();
            var textBounds = BoundsIn(card, text);
            var close = BoundsIn(card, CloseButton(card));
            var firstLineCenter = textBounds.Top + text.LineHeight / 2;
            Assert.InRange(close.Center.Y, firstLineCenter - 0.5, firstLineCenter + 0.5);
        }

        // One line of text with equal room above and below it: the close target adds no height.
        var line = BoundsIn(oneLine, oneLine.GetVisualDescendants().OfType<TextBlock>().First());
        Assert.InRange(line.Height, 17.5, 18.5);
        Assert.InRange(line.Top - (oneLine.Bounds.Height - line.Bottom), -0.5, 0.5);
        var paragraph = BoundsIn(wrapped, wrapped.GetVisualDescendants().OfType<TextBlock>().First());
        Assert.True(paragraph.Height >= 3 * 18 - 0.5, "The long result wraps onto several lines.");

        await CloseResultsTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task Pointer_input_reaches_the_panes_everywhere_but_on_a_result_card()
    {
        var (vm, window) = await OpenWindowWithNoteAsync();
        var host = Assert.IsType<Panel>(window.FindControl<Panel>("ResultsHost"));
        // Hit testing reads the composed scene, which a render tick brings up to date.
        bool HitsResults(Point point)
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return window.InputHitTest(point) is Visual hit
                && (hit == host || hit.GetVisualAncestors().Contains(host));
        }

        var attachments = BoundsIn(window, Assert.IsType<Border>(window.FindControl<Border>("AttachPane")));
        var corner = attachments.BottomRight - new Vector(40, 30);
        Assert.False(HitsResults(corner));

        await ReloadFromOutsideAsync(vm);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var card = BoundsIn(window, Assert.Single(ResultCards(window)));
        Assert.True(card.Contains(corner));
        Assert.True(HitsResults(corner));
        // The stack's padding beside and below the card belongs to the panes underneath.
        Assert.False(HitsResults(new Point(card.Left - 6, card.Center.Y)));
        Assert.False(HitsResults(new Point(card.Center.X, card.Bottom + 6)));

        await CloseResultsTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task The_result_host_announces_the_latest_result_with_its_severity()
    {
        var (vm, window) = await OpenWindowWithNoteAsync();
        var host = Assert.IsType<Panel>(window.FindControl<Panel>("ResultsHost"));
        Assert.Null(AutomationProperties.GetName(host));

        await ReloadFromOutsideAsync(vm);
        var reloaded = Assert.Single(vm.Results);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(host));
        Assert.Equal(reloaded.Message, AutomationProperties.GetName(host));

        File.Delete(BinderPath);
        Directory.CreateDirectory(BinderPath);
        vm.Editor.Title = "Unsaved title";
        await vm.SaveNowCommand.ExecuteAsync(null);
        var failure = vm.Results[0];
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(host));
        Assert.Equal(failure.Message, AutomationProperties.GetName(host));

        vm.DismissResult(failure);
        Assert.Null(AutomationProperties.GetName(host));

        await CloseResultsTestWindowAsync(vm, window);
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

    /// <summary>Counts rows the write-through store holds for <paramref name="path"/>, reading the store
    /// file directly. The caller closes the singleton first so its handle is released.</summary>
    private static int RowCountFor(string storeFile, string path)
    {
        using var connection = new SqliteConnection($"Data Source={storeFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM backups WHERE path = $path";
        // The store records the full absolute path (AtomicFile GetFullPath's before recording), so match it.
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private async Task AddThreeAttachmentsAsync(MainWindowViewModel vm)
    {
        var sources = Path.Combine(_home, "reorder-sources");
        Directory.CreateDirectory(sources);
        var files = Enumerable.Range(1, 3)
            .Select(index => Path.Combine(sources, $"attachment-{index}.txt"))
            .ToArray();
        foreach (var file in files)
        {
            File.WriteAllText(file, Path.GetFileName(file));
        }

        await vm.AddDroppedFiles(files);
        Assert.Equal(3, vm.Attachments.Count);
    }

    private async Task<(MainWindowViewModel Vm, MainWindow Window, ListBox List)> OpenWindowWithThreeAttachmentsAsync()
    {
        var vm = NewViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        await AddThreeAttachmentsAsync(vm);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return (vm, window, Assert.IsType<ListBox>(window.FindControl<ListBox>("AttachList")));
    }

    private async Task<(MainWindowViewModel Vm, MainWindow Window)> OpenWindowWithNoteAsync()
    {
        var vm = NewViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (vm, window);
    }

    /// <summary>
    /// Raises one result for every app-shell subject through its real trigger, oldest first, each
    /// with the longest copy it carries: the most results the policy can show at once. A missing
    /// binder and an applied text style are not here — they report at their own owners.
    /// </summary>
    private async Task ShowEveryShellResultAsync(MainWindowViewModel vm, MainWindow window)
    {
        var travel = Path.Combine(_home, "travel.daynote");
        _dialogs.BinderToCreate = travel;
        await vm.NewBinderCommand.ExecuteAsync(null);
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);

        var pickerFailure = new IOException("picker unavailable");
        _dialogs.OpenBinderPickerError = pickerFailure;
        await vm.OpenBinderCommand.ExecuteAsync(null);
        _dialogs.NewBinderPickerError = pickerFailure;
        await vm.NewBinderCommand.ExecuteAsync(null);
        _dialogs.OpenBinderPickerError = null;
        _dialogs.NewBinderPickerError = null;

        await ReloadFromOutsideAsync(vm);
        // A directory at the binder path makes every save fail until the test removes it.
        File.Delete(BinderPath);
        Directory.CreateDirectory(BinderPath);
        vm.Editor.Title = "Unsaved title";
        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal(4, vm.Results.Select(result => result.ResultKey).Distinct().Count());
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>
    /// Edits the open binder's file from outside and lets the window notice it, which is the shell's
    /// one piece of information: everything else it reports is a warning or a failure.
    /// </summary>
    private async Task ReloadFromOutsideAsync(MainWindowViewModel vm)
    {
        var store = new BinderStore();
        var outside = store.Load(BinderPath).Binder;
        outside.Notes[0].Title = "Changed outside";
        store.Save(BinderPath, outside);
        await vm.CheckExternalChangeAsync();
    }

    private static IReadOnlyList<Border> ResultCards(MainWindow window) =>
        Assert.IsType<ScrollViewer>(window.FindControl<ScrollViewer>("ResultsViewport"))
            .GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("shellResult"))
            .ToList();

    private static Button CloseButton(Border card) =>
        card.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("resultClose"));

    private static Rect BoundsIn(Visual target, Visual element) =>
        new(element.TranslatePoint(default, target)!.Value, element.Bounds.Size);

    private async Task CloseResultsTestWindowAsync(MainWindowViewModel vm, MainWindow window)
    {
        if (Directory.Exists(BinderPath))
        {
            Directory.Delete(BinderPath);
        }

        await CloseTestWindowAsync(vm, window);
    }

    /// <summary>
    /// Pumps the headless dispatcher until <paramref name="condition"/> holds, for asserting on the
    /// result of work that finishes on a background thread (its UI-thread continuation is not queued
    /// yet at the moment a real drag-drop event handler returns). Fails the test rather than hanging if
    /// the condition never becomes true.
    /// </summary>
    private static async Task PumpUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Condition was not met within the timeout.");
            }

            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task CloseTestWindowAsync(MainWindowViewModel vm, MainWindow window)
    {
        await vm.ShutdownAsync();
        window.DataContext = null;
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        // Close the backup store so its singleton re-opens against the next test's throwaway root and
        // releases the file handle before the directory is deleted.
        BackupStore.Close();
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
        public Exception? OpenPathError { get; set; }
        public Exception? NewBinderPickerError { get; set; }
        public Exception? OpenBinderPickerError { get; set; }
        public Exception? AttachmentPickerError { get; set; }
        public string? LastOpenedPath { get; private set; }

        public Task<string?> PickBinderToOpenAsync() => OpenBinderPickerError is null
            ? Task.FromResult(BinderToOpen)
            : Task.FromException<string?>(OpenBinderPickerError);
        public Task<string?> PickBinderToCreateAsync() => NewBinderPickerError is null
            ? Task.FromResult(BinderToCreate)
            : Task.FromException<string?>(NewBinderPickerError);
        public Task<IReadOnlyList<string>> PickAttachmentsAsync() => AttachmentPickerError is null
            ? Task.FromResult(AttachmentPaths)
            : Task.FromException<IReadOnlyList<string>>(AttachmentPickerError);
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false) => Task.FromResult(ConfirmResult);
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowAboutAsync() => Task.CompletedTask;
        public Task ShowShortcutsAsync() => Task.CompletedTask;
        public Task<bool> ShowSettingsAsync(AppConfig config, Func<AppConfig, bool> trySave) =>
            Task.FromResult(SettingsApplied && trySave(config));
        public Task<ExternalChangeChoice> AskExternalChangeAsync(string binderName) => Task.FromResult(ExternalChoice);
        public Task OpenPathExternallyAsync(string path)
        {
            LastOpenedPath = path;
            return OpenPathError is null
                ? Task.CompletedTask
                : Task.FromException(OpenPathError);
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
