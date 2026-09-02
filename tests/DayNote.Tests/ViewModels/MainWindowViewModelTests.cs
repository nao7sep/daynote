using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private MainWindowViewModel NewViewModel(Action<string>? deleteFile = null)
    {
        var vm = new MainWindowViewModel(new AppPaths(), _dialogs, new NullLogger(), deleteFile);
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
        vm.AddDroppedFiles(new[] { source });
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
        var firstResult = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Equal(OperationResultKind.Info, firstResult.Kind);
        Assert.True(firstResult.IsPersistent);
        Assert.Contains("Already attached", firstResult.Message);
        Assert.Empty(vm.Results);

        // A later file whose content the note already holds is not copied again.
        var c = Path.Combine(sources, "c.txt");
        File.WriteAllText(c, "same content");
        vm.AddDroppedFiles(new[] { c });
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

        vm.AddDroppedFiles(new[] { source });

        var error = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Equal(OperationResultKind.Error, error.Kind);
        Assert.True(error.IsPersistent);
        Assert.Equal(error.Message, error.AccessibleMessage);
        Assert.Equal(AutomationLiveSetting.Assertive, error.LiveSetting);

        File.Delete(assetsDirectory);
        vm.AddDroppedFiles(new[] { source });
        vm.AddDroppedFiles(new[] { source });

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
        vm.AddDroppedFiles([], unavailable: 1);

        var source = Path.Combine(_home, "source.txt");
        File.WriteAllText(source, "attachment content");
        var noteAssets = BinderStore.NoteAssetsDirectory(BinderPath, note.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(noteAssets)!);
        File.WriteAllText(noteAssets, "blocks the attachment directory");
        vm.AddDroppedFiles(new[] { source });

        File.Delete(noteAssets);
        vm.AddDroppedFiles(new[] { source });
        vm.AddDroppedFiles(new[] { source });
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

        var exception = Record.Exception(() => vm.AddDroppedFiles(new[] { source }));

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

        vm.AddDroppedFiles([], unavailable: 2);

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
        vm.AddDroppedFiles(new[] { source });
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
        vm.AddDroppedFiles(new[] { anotherSource });

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
        vm.AddDroppedFiles(new[] { source });
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
        AddThreeAttachments(vm);
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

        window.DragDrop(panePoint.Value, RawDragEventType.Drop, transfer, DragDropEffects.Copy, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsAttachmentDropActive);
        Assert.Contains(vm.Attachments, attachment => attachment.FileName == "headless-drop.txt");

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

    private void AddThreeAttachments(MainWindowViewModel vm)
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

        vm.AddDroppedFiles(files);
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
        AddThreeAttachments(vm);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        return (vm, window, Assert.IsType<ListBox>(window.FindControl<ListBox>("AttachList")));
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
