using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
using DayNote.I18n;
using DayNote.Logging;
using DayNote.Services;
using DayNote.ViewModels;
using DayNote.Views;
using DayNote.Tests.I18n;
using DayNote.Tests.Storage;
using Xunit;

namespace DayNote.Tests.ViewModels;

/// <summary>
/// The main view model orchestrates open/close, autosave, dirty tracking, and the known-binders list —
/// the logic where data loss would hide. [AvaloniaFact] runs each test on the headless UI thread (which
/// owns the DispatcherTimers); the storage root is relocated to a throwaway directory via DAYNOTE_DATA_DIR.
/// Joined to the AppPaths collection so that process-wide env var never races another test.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _home;
    private readonly string? _previousHome;
    private readonly FakeDialogService _dialogs = new();

    // Every time the view model records comes from here, so a test moves time explicitly.
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));

    public MainWindowViewModelTests()
    {
        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        _home = Path.Combine(Path.GetTempPath(), "daynote-vm-tests-" + IdGenerator.New());
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _home);
    }

    private string BinderPath => Path.Combine(_home, "test.daynote");

    // Every view model a test makes, so teardown can stop it even when the test failed before its own quit.
    private readonly List<MainWindowViewModel> _viewModels = [];

    // Every window a test opens, so teardown closes it even when the test failed before closing it.
    private readonly List<MainWindow> _windows = [];

    private MainWindowViewModel NewViewModel(
        Action<string>? deleteFile = null,
        Action<string>? deleteDirectory = null,
        BinderStore? binderStore = null,
        Action<string, string>? copyFile = null,
        IAppLogger? log = null)
    {
        var vm = new MainWindowViewModel(
            new AppPaths(), _dialogs, log ?? new NullLogger(), deleteFile, deleteDirectory, _clock, binderStore, copyFile);
        _viewModels.Add(vm);
        Assert.True(vm.IsReady);
        return vm;
    }

    [AvaloniaFact]
    public async Task An_attachment_file_that_could_not_be_deleted_is_reported_with_authored_copy()
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

        // The removal was saved first, so the note no longer refers to the file the delete left behind.
        Assert.DoesNotContain(item.FileName, vm.SelectedNote!.Note.Attachments);
        Assert.DoesNotContain(item.FileName, new BinderStore().Load(BinderPath).Binder.Notes.Single().Attachments);
        Assert.True(File.Exists(item.FullPath));
        var result = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Equal(OperationResultKind.Warning, result.Kind);
        Assert.Contains("could not be deleted", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("DAYNOTE-REMOVE-SENTINEL", result.Text, StringComparison.Ordinal);
        Assert.Empty(vm.Results);
        await vm.ShutdownAsync();
    }

    private async Task<MainWindowViewModel> OpenNewBinderAsync(BinderStore? binderStore = null, IAppLogger? log = null)
    {
        var vm = NewViewModel(binderStore: binderStore, log: log);
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
    public async Task A_zone_saved_in_settings_reaches_every_note_row_and_the_editor()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.NewNoteCommand.Execute(null);
        var created = vm.Notes.Select(row => row.Note.Created).ToArray();

        _dialogs.SettingsApplied = true;
        _dialogs.SettingsEdit = config => config.TimeZone = "Pacific/Kiritimati";
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        // Every row, not only the selected one, shows its time in the zone just saved (UTC+14).
        var kiritimati = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
        Assert.Equal(
            created.Select(time => DayNote.Core.Time.DayNoteTime.ToDisplay(time, kiritimati, Localizer.Current.Culture)),
            vm.Notes.Select(row => row.Subtitle));
        Assert.EndsWith(
            DayNote.Core.Time.DayNoteTime.ToSmartDisplay(vm.SelectedNote!.Note.Created, kiritimati, Localizer.Current.Culture, _clock.Now),
            vm.Editor.CreatedText);
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
        Assert.Contains("new-binder picker", newBinder.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("EACCES", newBinder.Text, StringComparison.Ordinal);

        _dialogs.NewBinderPickerError = null;
        _dialogs.OpenBinderPickerError = hostile;
        await vm.OpenBinderCommand.ExecuteAsync(null);
        Assert.Contains(vm.Results, result => result.Text.Contains("binder picker", StringComparison.Ordinal));

        _dialogs.OpenBinderPickerError = null;
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        _dialogs.AttachmentPickerError = hostile;
        await vm.AddAttachmentCommand.ExecuteAsync(null);

        Assert.NotNull(vm.AttachmentResult);
        Assert.Contains("attachment picker", vm.AttachmentResult!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("DAYNOTE-PICKER-SENTINEL", vm.AttachmentResult.Text, StringComparison.Ordinal);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Failed_external_reload_never_publishes_success()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        File.WriteAllText(BinderPath, "not valid DayNote data");
        vm.Editor.Body = "local edit";
        _dialogs.ExternalChoice = ExternalChangeChoice.ReloadFromDisk;

        await vm.SaveNowCommand.ExecuteAsync(null);

        var result = Assert.Single(
            vm.Results,
            item => item.Text.Contains("changed on disk", StringComparison.Ordinal));
        Assert.Equal(OperationResultKind.Error, result.Kind);
        Assert.DoesNotContain(
            vm.Results,
            item => item.Text.Contains("Reloaded after", StringComparison.Ordinal));
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task First_run_writes_no_config_and_changing_theme_writes_only_theme()
    {
        var configFile = Path.Combine(_home, "config.json");
        var vm = NewViewModel();
        Assert.False(File.Exists(configFile));
        Assert.False(File.Exists(Path.Combine(_home, "state.json")));
        _dialogs.SettingsApplied = true;
        _dialogs.SettingsEdit = config => config.Theme = ThemePreference.Dark;
        await vm.OpenSettingsCommand.ExecuteAsync(null);
        using var saved = JsonDocument.Parse(File.ReadAllText(configFile));
        Assert.Equal(new[] { "formatVersion", "theme" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("dark", saved.RootElement.GetProperty("theme").GetString());
        BackupStore.Close();
        Assert.Equal(1, RowCountFor(new AppPaths().BackupStoreFile, configFile));
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Saving_settings_keeps_the_binder_list_out_of_the_draft()
    {
        var vm = await OpenNewBinderAsync();
        var draftBinders = -1;
        _dialogs.SettingsApplied = true;
        _dialogs.SettingsEdit = config =>
        {
            draftBinders = config.Binders.Count;
            config.Theme = ThemePreference.Dark;
        };
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(0, draftBinders);
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(_home, "config.json")));
        Assert.Equal(new[] { "formatVersion", "theme", "binders" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Single(vm.Binders);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Binder_titles_are_config_and_deleting_config_does_not_reopen_a_stale_selection()
    {
        var vm = await OpenNewBinderAsync();
        var row = Assert.Single(vm.Binders);
        row.IsEditing = true;
        vm.ApplyBinderRename(row, "My binder");
        var configFile = Path.Combine(_home, "config.json");
        using (var saved = JsonDocument.Parse(File.ReadAllText(configFile)))
        {
            Assert.Equal(new[] { "formatVersion", "binders" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal("My binder", saved.RootElement.GetProperty("binders")[0].GetProperty("title").GetString());
        }
        using (var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(_home, "state.json"))))
        {
            Assert.False(state.RootElement.TryGetProperty("binders", out _));
            Assert.Equal(BinderPath, state.RootElement.GetProperty("currentBinderPath").GetString());
        }
        await vm.ShutdownAsync();
        BackupStore.Close();
        Assert.True(RowCountFor(new AppPaths().BackupStoreFile, configFile) > 0);
        Assert.Equal(0, RowCountFor(new AppPaths().BackupStoreFile, Path.Combine(_home, "state.json")));
        var restored = NewViewModel();
        Assert.Equal("My binder", Assert.Single(restored.Binders).Title);
        await restored.InitializeAsync();
        Assert.True(restored.HasBinder);
        await restored.ShutdownAsync();
        File.Delete(configFile);
        var reset = NewViewModel();
        await reset.InitializeAsync();
        Assert.Empty(reset.Binders);
        Assert.False(reset.HasBinder);
        Assert.False(File.Exists(configFile));
        await reset.ShutdownAsync();
    }

    // ----- Loading config.json and state.json, each on its own path ---------------------------------

    private string ConfigPath => Path.Combine(_home, "config.json");

    private string StatePath => Path.Combine(_home, "state.json");

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void WriteUnreadable(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetUnixFileMode(path, UnixFileMode.None);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void MakeReadable(string path) =>
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

    // Nothing in the data folder but the stores DayNote itself keeps: no set-aside copies.
    private string[] SetAsideCopies() => Directory.GetFiles(_home, "*.invalid");

    [AvaloniaFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task An_unreadable_state_file_keeps_the_settings_and_opens_on_the_default_view()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes are POSIX-only.");
        Directory.CreateDirectory(_home);
        File.WriteAllText(ConfigPath, """{"formatVersion":1,"theme":"dark"}""");
        WriteUnreadable(StatePath, """{"bindersPaneWidth":333}""");
        var log = new RecordingLogger();
        try
        {
            var vm = new MainWindowViewModel(new AppPaths(), _dialogs, log);
            _viewModels.Add(vm);
            await vm.InitializeAsync();

            Assert.True(vm.IsReady);
            Assert.Equal(ThemePreference.Dark, vm.Theme);
            Assert.Equal(new AppState().BindersPaneWidth, vm.BindersPaneWidth);
            Assert.Empty(_dialogs.Errors);
            Assert.Single(log.Entries, entry => entry.Level == "warn");
            Assert.DoesNotContain(log.Entries, entry => entry.Level == "error");
            await vm.ShutdownAsync();
        }
        finally
        {
            MakeReadable(StatePath);
        }
    }

    [AvaloniaFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task An_unreadable_settings_file_halts_names_its_path_and_is_left_in_place()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes are POSIX-only.");
        const string content = """{"formatVersion":1,"theme":"dark"}""";
        WriteUnreadable(ConfigPath, content);
        try
        {
            var vm = new MainWindowViewModel(new AppPaths(), _dialogs, new NullLogger());
            _viewModels.Add(vm);
            await vm.InitializeAsync();

            Assert.False(vm.IsReady);
            var (title, message) = Assert.Single(_dialogs.Errors);
            Assert.Equal("failure.startupDataTitle", title.Key);
            Assert.Contains(ConfigPath, English.Of(message), StringComparison.Ordinal);
            Assert.Empty(SetAsideCopies());
            await vm.ShutdownAsync();
        }
        finally
        {
            MakeReadable(ConfigPath);
        }

        Assert.Equal(content, File.ReadAllText(ConfigPath));
    }

    [AvaloniaTheory]
    [InlineData("{ not json")]
    [InlineData("""{"theme":"dark"}""")] // no format version
    [InlineData("""{"formatVersion":1,"theme":"dark","theme":"light"}""")] // a repeated key
    public async Task A_malformed_settings_file_halts_names_its_path_and_is_left_byte_identical(string content)
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(ConfigPath, content);
        var before = File.ReadAllBytes(ConfigPath);

        var vm = new MainWindowViewModel(new AppPaths(), _dialogs, new NullLogger());
        _viewModels.Add(vm);
        await vm.InitializeAsync();

        Assert.False(vm.IsReady);
        var (title, message) = Assert.Single(_dialogs.Errors);
        Assert.Equal("failure.startupDataTitle", title.Key);
        Assert.Equal("failure.startupSettingsMalformed", message.Key);
        Assert.Contains(ConfigPath, English.Of(message), StringComparison.Ordinal);
        await vm.ShutdownAsync();

        Assert.Empty(SetAsideCopies());
        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
    }

    [AvaloniaFact]
    public async Task A_corrupt_state_file_opens_on_the_default_view_and_the_next_state_save_replaces_it()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(StatePath, "{ not json");
        var log = new RecordingLogger();

        var vm = new MainWindowViewModel(new AppPaths(), _dialogs, log);
        _viewModels.Add(vm);
        await vm.InitializeAsync();

        Assert.True(vm.IsReady);
        Assert.Equal(new AppState().BindersPaneWidth, vm.BindersPaneWidth);
        Assert.Empty(_dialogs.Errors);
        Assert.Single(log.Entries, entry => entry.Level == "warn");
        await vm.ShutdownAsync();

        Assert.Empty(SetAsideCopies());
        Assert.NotNull(new JsonStore<AppState>(StatePath, FormatVersions.State).Load());
    }

    // ----- Stores written by a newer DayNote are reported and never written ------------------------

    [AvaloniaFact]
    public async Task A_newer_settings_file_halts_names_its_path_and_is_left_byte_identical()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(ConfigPath, $$"""{"formatVersion":{{FormatVersions.Config + 1}},"theme":"dark"}""");
        var before = File.ReadAllBytes(ConfigPath);

        var vm = new MainWindowViewModel(new AppPaths(), _dialogs, new NullLogger());
        _viewModels.Add(vm);
        await vm.InitializeAsync();

        Assert.False(vm.IsReady);
        var (title, message) = Assert.Single(_dialogs.Errors);
        Assert.Equal("failure.startupDataTitle", title.Key);
        Assert.Equal("failure.startupSettingsNewer", message.Key);
        Assert.Contains(ConfigPath, English.Of(message), StringComparison.Ordinal);
        await vm.ShutdownAsync();

        Assert.Empty(SetAsideCopies());
        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
    }

    [AvaloniaFact]
    public async Task A_newer_state_file_opens_on_the_default_view_with_one_warning_and_is_replaced()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(StatePath, $$"""{"formatVersion":{{FormatVersions.State + 1}},"bindersPaneWidth":333}""");
        var before = File.ReadAllBytes(StatePath);
        var log = new RecordingLogger();

        var vm = new MainWindowViewModel(new AppPaths(), _dialogs, log);
        _viewModels.Add(vm);
        await vm.InitializeAsync();
        Assert.True(vm.IsReady);
        Assert.Equal(new AppState().BindersPaneWidth, vm.BindersPaneWidth);

        // The view state is disposable: the next state save replaces the newer file.
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        await vm.ShutdownAsync();

        Assert.Empty(_dialogs.Errors);
        Assert.Single(log.Entries, entry => entry.Level == "warn");
        Assert.Empty(SetAsideCopies());
        Assert.NotEqual(before, File.ReadAllBytes(StatePath));
        Assert.Equal(BinderPath, new JsonStore<AppState>(StatePath, FormatVersions.State).Load()!.CurrentBinderPath);
    }

    private string NewerBinderText =>
        $"format_version = {FormatVersions.Binder + 1}\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nbody = 'from the future'\n";

    [AvaloniaFact]
    public async Task A_newer_binder_is_not_opened_is_named_and_is_left_byte_identical()
    {
        var vm = NewViewModel();
        File.WriteAllText(BinderPath, NewerBinderText);
        var before = File.ReadAllBytes(BinderPath);

        _dialogs.BinderToOpen = BinderPath;
        await vm.OpenBinderCommand.ExecuteAsync(null);

        Assert.False(vm.HasBinder);
        var (title, message) = Assert.Single(_dialogs.Errors);
        Assert.Equal("binder.openFailedTitle", title.Key);
        Assert.Equal("failure.openBinderNewer", message.Key);
        Assert.Contains("“test”", English.Of(message), StringComparison.Ordinal);
        await vm.ShutdownAsync();

        Assert.Equal(before, File.ReadAllBytes(BinderPath));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_save_before_the_next_check_never_writes_over_a_binder_a_newer_DayNote_wrote(bool quitting)
    {
        // A sync client delivers a newer binder, and the save or quit lands before any check reads it.
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        vm.Editor.Body = "typed before the delivery";
        File.WriteAllText(BinderPath, NewerBinderText);
        var before = File.ReadAllBytes(BinderPath);

        if (quitting)
        {
            // The quit stops once, so the closed binder's notice is seen before the window goes.
            Assert.False(await vm.ShutdownAsync());
        }
        else
        {
            await vm.SaveNowCommand.ExecuteAsync(null);
        }

        Assert.False(vm.HasBinder);
        var result = Assert.Single(vm.Results);
        Assert.Contains("newer version of DayNote", result.Text, StringComparison.Ordinal);
        Assert.True(await vm.ShutdownAsync());
        Assert.Equal(before, File.ReadAllBytes(BinderPath));
    }

    [AvaloniaFact]
    public async Task A_binder_whose_notes_share_an_id_is_not_opened_is_named_and_is_left_byte_identical()
    {
        // A hand edit that copied a note: assigning either one a fresh id would part it from its files.
        var vm = NewViewModel();
        File.WriteAllText(BinderPath,
            "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nbody = 'one'\n\n[[note]]\nid = \"n1\"\nbody = 'two'\n");
        var before = File.ReadAllBytes(BinderPath);

        _dialogs.BinderToOpen = BinderPath;
        await vm.OpenBinderCommand.ExecuteAsync(null);

        Assert.False(vm.HasBinder);
        var (_, message) = Assert.Single(_dialogs.Errors);
        Assert.Equal("failure.openBinderFormat", message.Key);
        Assert.Contains("“test”", English.Of(message), StringComparison.Ordinal);
        await vm.ShutdownAsync();

        Assert.Equal(before, File.ReadAllBytes(BinderPath));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_open_binder_rewritten_by_a_newer_DayNote_is_never_written(bool unsavedEdits)
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        File.WriteAllText(BinderPath, NewerBinderText);
        var before = File.ReadAllBytes(BinderPath);
        if (unsavedEdits)
        {
            vm.Editor.Body = "typed after the rewrite";
        }

        await vm.SaveNowCommand.ExecuteAsync(null);

        // Nothing watches the file: an unedited binder stays open until its next save, which is the
        // moment the newer file is found and closed without being written.
        Assert.Equal(!unsavedEdits, vm.HasBinder);
        Assert.Equal(0, _dialogs.ExternalChangeQuestions);
        if (unsavedEdits)
        {
            var result = Assert.Single(vm.Results);
            Assert.Equal(OperationResultKind.Error, result.Kind);
            Assert.Contains("newer version of DayNote", result.Text, StringComparison.Ordinal);
        }

        await vm.ShutdownAsync();

        Assert.Equal(before, File.ReadAllBytes(BinderPath));
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

    // ----- A binder changed outside DayNote is noticed when DayNote next saves it -----------------------

    private void ChangeOutside(string title)
    {
        var store = new BinderStore();
        var outside = store.Load(BinderPath).Binder;
        outside.Notes[0].Title = title;
        store.Save(BinderPath, outside);
    }

    [AvaloniaFact]
    public async Task An_outside_change_is_asked_about_before_anything_is_written_and_keep_mine_writes_once()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        ChangeOutside("Changed outside");
        var outside = File.ReadAllBytes(BinderPath);

        var answer = new TaskCompletionSource<ExternalChangeChoice>();
        _dialogs.ExternalAnswer = () =>
        {
            // While the question is open, nothing has replaced the outside version.
            Assert.Equal(outside, File.ReadAllBytes(BinderPath));
            return answer.Task;
        };
        vm.Editor.Body = "local edit";
        var save = vm.SaveNowCommand.ExecuteAsync(null);
        var secondSave = vm.SaveNowCommand.ExecuteAsync(null);
        Assert.False(save.IsCompleted);
        answer.SetResult(ExternalChangeChoice.KeepMine);
        await save;
        await secondSave;

        // One question for one change, however many saves found it; the local version wins.
        Assert.Equal(1, _dialogs.ExternalChangeQuestions);
        var saved = new BinderStore().Load(BinderPath).Binder.Notes[0];
        Assert.Equal("local edit", saved.Body);
        Assert.Equal("Saved", vm.SaveStateText);

        // The app's own write is the new baseline, so the next save asks nothing.
        vm.Editor.Body = "another local edit";
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(1, _dialogs.ExternalChangeQuestions);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Keep_mine_replaces_only_the_version_it_asked_about()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        ChangeOutside("First outside change");
        var answers = 0;
        _dialogs.ExternalAnswer = () =>
        {
            if (++answers == 1)
            {
                // Another change lands while the user decides about the first.
                ChangeOutside("Second outside change");
            }

            return Task.FromResult(answers == 1 ? ExternalChangeChoice.KeepMine : ExternalChangeChoice.ReloadFromDisk);
        };

        vm.Editor.Body = "local edit";
        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal(2, _dialogs.ExternalChangeQuestions);
        Assert.Equal("Second outside change", new BinderStore().Load(BinderPath).Binder.Notes[0].Title);
        Assert.Equal("Second outside change", vm.Editor.Title);
        Assert.Equal("Saved", vm.SaveStateText);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Reload_adopts_the_outside_version_and_discards_the_unsaved_edit()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        ChangeOutside("Changed outside");
        var outside = File.ReadAllBytes(BinderPath);
        _dialogs.ExternalChoice = ExternalChangeChoice.ReloadFromDisk;

        vm.Editor.Body = "local edit";
        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal("Changed outside", vm.Editor.Title);
        Assert.Equal(string.Empty, vm.Editor.Body);
        Assert.Equal("Saved", vm.SaveStateText);
        Assert.Equal(outside, File.ReadAllBytes(BinderPath));
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_binder_file_deleted_outside_is_written_again_by_the_next_save()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);

        File.Delete(BinderPath);
        vm.Editor.Title = "Written again";
        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal("Written again", new BinderStore().Load(BinderPath).Binder.Notes[0].Title);
        Assert.Equal(0, _dialogs.ExternalChangeQuestions);
        Assert.Empty(vm.Results);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_quit_that_finds_an_outside_change_writes_nothing_and_asks_whether_to_quit()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        ChangeOutside("Changed outside");
        var outside = File.ReadAllBytes(BinderPath);
        vm.Editor.Body = "typed before quitting";

        // Stay: the quit is cancelled with nothing written, and no Keep or Reload question is asked.
        Assert.False(await vm.QuitAsync());
        Assert.Single(_dialogs.QuitQuestions);
        Assert.Equal(0, _dialogs.ExternalChangeQuestions);
        Assert.Equal(outside, File.ReadAllBytes(BinderPath));
        Assert.True(vm.HasBinder);

        // Back in the app, the next save asks which version wins.
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(1, _dialogs.ExternalChangeQuestions);
        Assert.Equal("typed before quitting", new BinderStore().Load(BinderPath).Binder.Notes[0].Body);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task An_ending_session_leaves_an_outside_change_in_place_and_asks_nothing()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        ChangeOutside("Changed outside");
        var outside = File.ReadAllBytes(BinderPath);
        vm.Editor.Body = "typed before logout";

        await vm.EndSessionAsync();

        Assert.Empty(_dialogs.QuitQuestions);
        Assert.Equal(0, _dialogs.ExternalChangeQuestions);
        Assert.Equal(outside, File.ReadAllBytes(BinderPath));
    }

    // ----- Quitting (unsaved-edits-conventions, Quitting) -----

    private string SavedBody() => new BinderStore().Load(BinderPath).Binder.Notes[0].Body;

    private async Task<(MainWindowViewModel Vm, GatedBinderStore Store, ListLogger Log)> OpenWithUnsavedEditAsync(string body)
    {
        var store = new GatedBinderStore();
        var log = new ListLogger();
        var vm = await OpenNewBinderAsync(store, log);
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        vm.Editor.Body = body;
        return (vm, store, log);
    }

    [AvaloniaFact]
    public async Task A_quit_saves_the_pending_edit_and_asks_nothing()
    {
        var (vm, _, _) = await OpenWithUnsavedEditAsync("typed before quitting");

        Assert.True(await vm.QuitAsync());

        Assert.Empty(_dialogs.QuitQuestions);
        Assert.Equal("typed before quitting", SavedBody());
    }

    [AvaloniaFact]
    public async Task A_quit_whose_save_fails_stops_and_Retry_saves_and_quits()
    {
        var (vm, store, _) = await OpenWithUnsavedEditAsync("kept through a retry");
        store.SaveFailure = new IOException("No space left on device");
        _dialogs.QuitAnswer = () =>
        {
            // The user frees some space before choosing Retry.
            store.SaveFailure = null;
            return UnsavedQuitChoice.Retry;
        };

        Assert.True(await vm.QuitAsync());

        Assert.Equal(new[] { "test" }, _dialogs.QuitQuestions);
        Assert.Equal("kept through a retry", SavedBody());
    }

    [AvaloniaFact]
    public async Task Quit_anyway_after_a_failed_save_exits_without_the_edit_and_logs_it()
    {
        var (vm, store, log) = await OpenWithUnsavedEditAsync("lost by choice");
        store.SaveFailure = new IOException("No space left on device");
        _dialogs.QuitAnswer = () => UnsavedQuitChoice.QuitAnyway;

        Assert.True(await vm.QuitAsync());

        Assert.Single(_dialogs.QuitQuestions);
        Assert.Equal(string.Empty, SavedBody());
        Assert.Contains(("warn", "Quitting with the binder's edits unsaved"), log.Entries);
    }

    [AvaloniaFact]
    public async Task Dismissing_the_quit_question_keeps_the_app_open_with_the_edit_unsaved()
    {
        var (vm, store, _) = await OpenWithUnsavedEditAsync("still here");
        store.SaveFailure = new IOException("No space left on device");

        Assert.False(await vm.QuitAsync());

        Assert.Single(_dialogs.QuitQuestions);
        Assert.True(vm.HasBinder);
        Assert.Equal("still here", vm.Editor.Body);
        Assert.True(vm.IsSaveStateError);

        // Once the location can be written again, the next quit saves and goes ahead.
        store.SaveFailure = null;
        Assert.True(await vm.QuitAsync());
        Assert.Equal("still here", SavedBody());
    }

    [AvaloniaFact]
    public async Task A_quit_whose_save_stalls_stops_at_its_bound_and_asks()
    {
        var (vm, store, log) = await OpenWithUnsavedEditAsync("written once the volume answers");
        var stalled = store.HoldNextSave();
        _dialogs.QuitAnswer = () =>
        {
            // The volume answers again before the user chooses Retry.
            stalled.Release();
            return UnsavedQuitChoice.Retry;
        };

        var quit = vm.QuitAsync();
        await stalled.Entered.Task;
        _clock.Advance(MainWindowViewModel.QuitBinderSaveBound - TimeSpan.FromTicks(1));
        Dispatcher.UIThread.RunJobs();
        Assert.False(quit.IsCompleted);
        Assert.Empty(_dialogs.QuitQuestions);

        _clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(await quit);

        Assert.Single(_dialogs.QuitQuestions);
        Assert.Contains(("error", "Binder save did not finish within the quit's bound"), log.Entries);
        Assert.Equal("written once the volume answers", SavedBody());
    }

    [AvaloniaFact]
    public async Task The_session_ending_with_a_failed_save_asks_nothing_and_logs_it()
    {
        var (vm, store, log) = await OpenWithUnsavedEditAsync("lost at logout");
        store.SaveFailure = new IOException("No space left on device");

        await vm.EndSessionAsync();

        Assert.Empty(_dialogs.QuitQuestions);
        Assert.Contains(("error", "Failed to save binder"), log.Entries);
        Assert.Contains(("error", "Session ended with the binder's edits unsaved"), log.Entries);
    }

    [AvaloniaFact]
    public async Task The_session_ending_with_a_stalled_save_ends_at_its_bound_and_asks_nothing()
    {
        var (vm, store, log) = await OpenWithUnsavedEditAsync("still writing at logout");
        var stalled = store.HoldNextSave();

        var ending = vm.EndSessionAsync();
        await stalled.Entered.Task;
        _clock.Advance(MainWindowViewModel.QuitBinderSaveBound);
        await ending;

        Assert.Empty(_dialogs.QuitQuestions);
        Assert.Contains(("error", "Binder save did not finish within the quit's bound"), log.Entries);
        Assert.Contains(("error", "Session ended with the binder's edits unsaved"), log.Entries);
        stalled.Release();
    }

    [AvaloniaFact]
    public async Task The_session_ending_with_the_edit_saved_logs_no_failure()
    {
        var (vm, _, log) = await OpenWithUnsavedEditAsync("saved at logout");

        await vm.EndSessionAsync();

        Assert.Equal("saved at logout", SavedBody());
        Assert.DoesNotContain(log.Entries, entry => entry.Level == "error");
    }

    [AvaloniaFact]
    public async Task A_state_file_the_quit_cannot_write_is_logged_and_the_quit_goes_ahead()
    {
        var (vm, _, log) = await OpenWithUnsavedEditAsync("saved although the state is not");
        // A folder where state.json belongs: the write cannot replace it.
        var statePath = new AppPaths().StateFile;
        File.Delete(statePath);
        Directory.CreateDirectory(statePath);
        log.Entries.Clear();

        Assert.True(await vm.QuitAsync());

        Assert.Empty(_dialogs.QuitQuestions);
        Assert.Contains(("error", "Failed to save state"), log.Entries);
        Assert.Equal("saved although the state is not", SavedBody());
    }

    [AvaloniaFact]
    public async Task A_quit_writes_the_state_once_with_what_changed_after_it_began()
    {
        var vm = await OpenNewBinderAsync();
        var statePath = new AppPaths().StateFile;
        var before = File.ReadAllText(statePath);

        // The Records window closes after the quit begins; its placement waits for the quit's write.
        vm.BeginShutdown();
        vm.SaveRecordsWindowPlacement(new WindowPlacement(40, 50, 700, 500, false));
        Assert.Equal(before, File.ReadAllText(statePath));

        Assert.True(await vm.ShutdownAsync());
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(statePath), DayNoteJson.Options)!;
        Assert.Equal(700, state.RecordsWindowWidth);
        Assert.Equal(BinderPath, state.CurrentBinderPath);
    }

    [AvaloniaFact]
    public async Task A_switch_refused_by_a_failed_save_leaves_the_open_binder_selected()
    {
        var store = new GatedBinderStore();
        var vm = NewViewModel(binderStore: store);
        var travel = Path.Combine(_home, "travel.daynote");
        _dialogs.BinderToCreate = travel;
        await vm.NewBinderCommand.ExecuteAsync(null);
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        vm.Editor.Body = "unsaved";
        store.SaveFailure = new IOException("No space left on device");

        // Clicking the other row selects it, and the switch it starts is refused.
        var other = vm.Binders.Single(binder => PathKey.Equal(binder.Path, travel));
        vm.SelectedBinder = other;
        await vm.OpenKnownBinderCommand.ExecuteAsync(other);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.HasBinder);
        Assert.Equal("unsaved", vm.Editor.Body);
        Assert.True(PathKey.Equal(BinderPath, vm.SelectedBinder!.Path));
        store.SaveFailure = null;
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Closing_the_main_window_runs_the_same_quit_and_stays_open_when_the_user_keeps_it()
    {
        var (vm, store, _) = await OpenWithUnsavedEditAsync("kept by closing the question");
        store.SaveFailure = new IOException("No space left on device");
        var window = new MainWindow { DataContext = vm };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Close();
        await PumpUntilAsync(() => _dialogs.QuitQuestions.Count == 1);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsVisible);
        Assert.True(vm.HasBinder);

        store.SaveFailure = null;
        window.Close();
        await PumpUntilAsync(() => !window.IsVisible);
        Assert.Equal("kept by closing the question", SavedBody());
    }

    [AvaloniaFact]
    public async Task The_session_ending_closes_the_main_window_within_the_bound_without_asking()
    {
        var (vm, store, _) = await OpenWithUnsavedEditAsync("still writing as the session ends");
        var stalled = store.HoldNextSave();
        var window = new MainWindow { DataContext = vm };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The system waits on the close itself, so the bound has to run out while the close is waiting.
        Dispatcher.UIThread.Post(async () =>
        {
            await stalled.Entered.Task;
            _clock.Advance(MainWindowViewModel.QuitBinderSaveBound);
        });
        CloseAs(window, WindowCloseReason.OSShutdown);

        Assert.False(window.IsVisible);
        Assert.Empty(_dialogs.QuitQuestions);
        stalled.Release();
    }

    // What the lifetime does when the system ends the session; Avalonia offers no public way to say why.
    private static void CloseAs(Window window, WindowCloseReason reason) =>
        typeof(Window).GetMethod("CloseCore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, [reason, false, false]);

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
        Assert.Contains("Already attached", firstResult.Text);
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
        Assert.Equal(error.Text, error.AccessibleMessage);
        Assert.Equal(AutomationLiveSetting.Assertive, error.LiveSetting);

        File.Delete(assetsDirectory);
        await vm.AddDroppedFiles(new[] { source });
        await vm.AddDroppedFiles(new[] { source });

        var information = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Equal(OperationResultKind.Info, information.Kind);
        Assert.True(information.IsPersistent);
        Assert.Equal(information.Text, information.AccessibleMessage);
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
        Assert.StartsWith("Your changes are still in DayNote", firstFailure.Text);

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
        Assert.Contains("Could not prepare", result.Text);
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
        Assert.Equal("2 items are not readable local files.", result.Text);
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
        Assert.Contains("Double-click to try again", failure.Text);
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
        Assert.Contains("unavailable on disk", result.Text);
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
        using var sets = JsonDocument.Parse(File.ReadAllText(configPath));
        Assert.Equal(new[] { "formatVersion", "textStyles" }, sets.RootElement.EnumerateObject().Select(property => property.Name));

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
        _windows.Add(window);
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
        var saved = JsonSerializer.Deserialize<AppConfig>(
            File.ReadAllText(Path.Combine(_home, "config.json")), DayNoteJson.Options)!;
        Assert.Equal(
            new[] { "hidden.daynote", "alpha.daynote", "alpine.daynote" },
            saved.Binders.Select(entry => Path.GetFileName(entry.Path)));

        await CloseTestWindowAsync(vm, window);
    }

    [AvaloniaFact]
    public async Task Commands_that_change_the_settings_keep_what_was_saved_when_the_save_fails()
    {
        var vm = NewViewModel();
        foreach (var name in new[] { "one", "two" })
        {
            _dialogs.BinderToCreate = Path.Combine(_home, name + ".daynote");
            await vm.NewBinderCommand.ExecuteAsync(null);
        }

        // A folder where the settings file belongs makes every settings save fail.
        var configPath = Path.Combine(_home, "config.json");
        var saved = File.ReadAllText(configPath);
        File.Delete(configPath);
        Directory.CreateDirectory(configPath);
        var order = vm.BinderOrder();
        var style = vm.EditorFontFamily;

        vm.CycleTextStyleCommand.Execute(null);
        Assert.Equal(style, vm.EditorFontFamily);
        Assert.Equal(string.Empty, vm.TextStyleStatusText);
        Assert.Contains("text style could not be changed", Assert.Single(vm.Results).Text, StringComparison.Ordinal);

        var row = vm.Binders[0];
        var title = row.Title;
        row.IsEditing = true;
        vm.ApplyBinderRename(row, "Renamed");
        Assert.Equal(title, row.Title);
        Assert.Contains("new name could not be saved", Assert.Single(vm.Results).Text, StringComparison.Ordinal);

        Assert.True(vm.MoveBinder(order[0], order[1]));
        vm.CommitBinderOrder();
        Assert.Equal(order.Select(item => item.Path), vm.BinderOrder().Select(item => item.Path));
        Assert.Contains("order could not be saved", Assert.Single(vm.Results).Text, StringComparison.Ordinal);

        await vm.RemoveBinderCommand.ExecuteAsync(order[0]);
        Assert.Equal(order.Select(item => item.Path), vm.BinderOrder().Select(item => item.Path));
        Assert.Contains("could not be removed from the list", Assert.Single(vm.Results).Text, StringComparison.Ordinal);

        // A binder that could not be listed is still open; the list only says it was not added.
        var third = Path.Combine(_home, "three.daynote");
        _dialogs.BinderToCreate = third;
        await vm.NewBinderCommand.ExecuteAsync(null);
        Assert.True(vm.HasBinder);
        Assert.DoesNotContain(vm.BinderOrder(), item => PathKey.Equal(item.Path, third));
        var added = Assert.Single(vm.Results);
        Assert.Equal(OperationResultKind.Warning, added.Kind);
        Assert.Contains("could not be added to the binder list", added.Text, StringComparison.Ordinal);

        await vm.ShutdownAsync();
        Directory.Delete(configPath);
        File.WriteAllText(configPath, saved);
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

        var configPath = Path.Combine(_home, "config.json");
        var before = File.ReadAllText(configPath);
        var start = vm.BinderOrder();

        Assert.True(vm.MoveBinder(start[0], start[2]));
        Assert.Equal(new[] { start[1], start[2], start[0] }, vm.BinderOrder());
        Assert.True(vm.RestoreBinderOrder(start));
        vm.CommitBinderOrder();

        Assert.Equal(start, vm.BinderOrder());
        Assert.Equal(start, vm.Binders);
        Assert.Equal(before, File.ReadAllText(configPath));

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Shell_results_keep_one_card_per_subject_with_the_newest_on_top()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);

        // A binder that opens but cannot be remembered is a warning that stays.
        await ShowBinderNotRememberedAsync(vm);
        var notRemembered = Assert.Single(vm.Results);
        Assert.Equal(OperationResultKind.Warning, notRemembered.Kind);
        Assert.True(notRemembered.IsPersistent);
        Assert.Same(notRemembered, vm.AnnouncedResult);

        // Another subject goes on top and leaves the first card where it stands.
        _dialogs.OpenBinderPickerError = new IOException("picker unavailable");
        await vm.OpenBinderCommand.ExecuteAsync(null);
        var picker = vm.Results[0];
        Assert.Equal(2, vm.Results.Count);
        Assert.Same(notRemembered, vm.Results[1]);
        Assert.Same(picker, vm.AnnouncedResult);

        // Repeating that failure is neither a second card nor a second announcement.
        await vm.OpenBinderCommand.ExecuteAsync(null);
        _dialogs.OpenBinderPickerError = null;
        Assert.Equal(2, vm.Results.Count);
        Assert.Same(picker, vm.Results[0]);

        // A later message replaces its subject's card in place, whatever its severity: the settings
        // file is one subject whether a binder could not be remembered or a rename could not be saved.
        var configFile = Path.Combine(_home, "config.json");
        File.Delete(configFile);
        Directory.CreateDirectory(configFile);
        var row = vm.Binders.Single(binder => PathKey.Equal(binder.Path, BinderPath));
        row.IsEditing = true;
        vm.ApplyBinderRename(row, "Renamed");
        var warning = vm.Results[1];
        Assert.Equal(OperationResultKind.Error, warning.Kind);
        Assert.True(warning.IsPersistent);

        // The same unresolved problem again is neither a second card nor a second announcement.
        row.IsEditing = true;
        vm.ApplyBinderRename(row, "Renamed");
        Assert.Same(warning, vm.Results[1]);
        Assert.Equal(2, vm.Results.Count);
        Directory.Delete(configFile);

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
    public async Task An_added_attachment_is_recorded_once_and_a_duplicate_adds_nothing()
    {
        var vm = await OpenNewBinderAsync();
        var photo = Path.Combine(_home, "photo.bin");
        File.WriteAllBytes(photo, [1, 2, 3]);
        var sameBytes = Path.Combine(_home, "copy-of-photo.bin");
        File.WriteAllBytes(sameBytes, [1, 2, 3]);

        vm.NewNoteCommand.Execute(null);
        var note = vm.SelectedNote!.Note;
        _dialogs.AttachmentPaths = [photo];
        await vm.AddAttachmentCommand.ExecuteAsync(null);
        _dialogs.AttachmentPaths = [sameBytes];
        await vm.AddAttachmentCommand.ExecuteAsync(null);

        var added = Path.Combine(BinderStore.NoteAssetsDirectory(BinderPath, note.Id), "photo.bin");
        BackupStore.Close();
        Assert.Equal(1, RowCountFor(new AppPaths().BackupStoreFile, added));
        Assert.Single(Directory.GetFiles(BinderStore.NoteAssetsDirectory(BinderPath, note.Id)));
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
    public async Task Attachment_files_are_deleted_only_after_the_binder_stops_referring_to_them()
    {
        var vm = await OpenNewBinderAsync();
        var first = Path.Combine(_home, "first.txt");
        var second = Path.Combine(_home, "second.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        vm.NewNoteCommand.Execute(null);
        var note = vm.SelectedNote!.Note;
        _dialogs.AttachmentPaths = [first, second];
        await vm.AddAttachmentCommand.ExecuteAsync(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        var assets = BinderStore.NoteAssetsDirectory(BinderPath, note.Id);
        var removed = vm.Attachments[0].FullPath;

        // A folder where the binder file belongs makes every save fail.
        File.Delete(BinderPath);
        Directory.CreateDirectory(BinderPath);

        await vm.RemoveAttachmentCommand.ExecuteAsync(vm.Attachments[0]);
        Assert.True(File.Exists(removed));
        Assert.Equal(OperationResultKind.Warning, Assert.IsType<OperationResultViewModel>(vm.AttachmentResult).Kind);

        await vm.DeleteNoteCommand.ExecuteAsync(null);
        Assert.Empty(vm.Notes);
        Assert.True(Directory.Exists(assets));
        Assert.Contains(vm.Results, result => result.Text.Contains("attachment files", StringComparison.Ordinal));

        // Once a save can land, a removal saves the binder before its file goes.
        Directory.Delete(BinderPath);
        vm.NewNoteCommand.Execute(null);
        _dialogs.AttachmentPaths = [first];
        await vm.AddAttachmentCommand.ExecuteAsync(null);
        var attached = vm.Attachments.Single().FullPath;
        await vm.RemoveAttachmentCommand.ExecuteAsync(vm.Attachments.Single());
        Assert.False(File.Exists(attached));
        Assert.Empty(new BinderStore().Load(BinderPath).Binder.Notes.Single().Attachments);

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
        Assert.Contains("attachment files", warning.Text, StringComparison.Ordinal);
        Assert.True(Directory.Exists(BinderStore.NoteAssetsDirectory(BinderPath, note.Id)));

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_locked_note_keeps_its_attachments_while_its_status_stays_free()
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
        var kept = vm.Attachments.Select(attachment => attachment.FileName).ToArray();
        Assert.Equal(2, kept.Length);

        // Publishing alone locks nothing.
        vm.Editor.Status = NoteStatus.Published;
        Assert.True(vm.CanEditNote);

        // A locked note's text cannot be edited, and neither can the set of files it carries: nothing adds,
        // removes, or reorders them until it is unlocked. Its status still moves.
        vm.Editor.Locked = true;
        Assert.False(vm.CanEditNote);
        Assert.False(vm.MoveAttachment(vm.Attachments[1], 0));
        _dialogs.AttachmentPaths = [late];
        await vm.AddAttachmentCommand.ExecuteAsync(null);
        await vm.AddDroppedFiles([late]);
        await vm.RemoveAttachmentCommand.ExecuteAsync(vm.Attachments[0]);
        Assert.Equal(kept, vm.Attachments.Select(attachment => attachment.FileName));
        vm.Editor.Status = NoteStatus.Retired;
        Assert.Equal(NoteStatus.Retired, vm.SelectedNote!.Note.Status);

        // Unlocked, all three work again.
        vm.Editor.Locked = false;
        Assert.True(vm.CanEditNote);
        Assert.True(vm.MoveAttachment(vm.Attachments[1], 0));
        await vm.AddDroppedFiles([late]);
        Assert.Equal(3, vm.Attachments.Count);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_note_locked_while_its_files_are_copied_does_not_take_them()
    {
        var copying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var vm = NewViewModel(copyFile: (source, destination) =>
        {
            copying.TrySetResult();
            release.Wait();
            AtomicFile.CopyNew(source, destination);
        });
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        var note = vm.SelectedNote!.Note;
        var file = Path.Combine(_home, "large.bin");
        File.WriteAllText(file, "large");

        var adding = vm.AddDroppedFiles([file]);
        await copying.Task;
        vm.Editor.Locked = true;
        release.Set();
        await adding;

        Assert.Empty(note.Attachments);
        Assert.Empty(vm.Attachments);
        Assert.Empty(Directory.GetFiles(BinderStore.NoteAssetsDirectory(BinderPath, note.Id)));
        var result = Assert.IsType<OperationResultViewModel>(vm.AttachmentResult);
        Assert.Contains("locked while the files were being added", result.Text, StringComparison.Ordinal);
        Assert.True(File.Exists(file));

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_locked_note_can_still_be_deleted()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Locked = true;
        _dialogs.ConfirmResult = true;

        await vm.DeleteNoteCommand.ExecuteAsync(null);

        Assert.Empty(vm.Notes);
        await vm.ShutdownAsync();
    }

    // Modified moves on content edits and on nothing else (content-lifecycle-conventions).

    private Note SavedNote() => new BinderStore().Load(BinderPath).Binder.Notes.Single();

    [AvaloniaFact]
    public async Task An_untouched_new_note_keeps_its_creation_time_as_Modified()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);

        await vm.SaveNowCommand.ExecuteAsync(null);

        var saved = SavedNote();
        Assert.Equal(saved.Created, saved.Modified);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_new_binder_records_one_instant_as_created_and_modified()
    {
        var vm = await OpenNewBinderAsync();

        var binder = new BinderStore().Load(BinderPath).Binder;

        Assert.Equal(binder.Created, binder.Modified);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Modified_is_the_time_of_the_edit_not_of_the_save()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var edited = _clock.Now;
        vm.Editor.Body = "words";
        _clock.Advance(TimeSpan.FromMinutes(1));

        // A status change after the edit, still unsaved, leaves the edit's time in place.
        vm.Editor.Status = NoteStatus.Verified;
        _clock.Advance(TimeSpan.FromMinutes(1));
        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal(edited, SavedNote().Modified);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Status_changes_and_locking_leave_Modified_alone()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Body = "words";
        await vm.SaveNowCommand.ExecuteAsync(null);
        var modified = SavedNote().Modified;

        foreach (var status in new[] { NoteStatus.Verified, NoteStatus.Published, NoteStatus.Retired, NoteStatus.Published })
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            vm.Editor.Status = status;
            await vm.SaveNowCommand.ExecuteAsync(null);
            Assert.Equal(status, SavedNote().Status);
            Assert.Equal(modified, SavedNote().Modified);
        }

        vm.Editor.Locked = true;
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.True(SavedNote().Locked);
        Assert.Equal(modified, SavedNote().Modified);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Edits_that_leave_the_content_as_saved_leave_Modified_alone()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Title = "Title";
        vm.Editor.Body = "body";
        await vm.SaveNowCommand.ExecuteAsync(null);
        var modified = SavedNote().Modified;
        _clock.Advance(TimeSpan.FromMinutes(1));

        // Typed, then undone before the save.
        vm.Editor.Title = "Title changed";
        vm.Editor.Title = "Title";
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(modified, SavedNote().Modified);

        // Trailing whitespace the save's own cleanup removes.
        vm.Editor.Body = "body   ";
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(modified, SavedNote().Modified);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Any_content_edit_moves_Modified_whatever_the_status()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Body = "words";
        vm.Editor.Status = NoteStatus.Published;
        await vm.SaveNowCommand.ExecuteAsync(null);
        var before = SavedNote();
        _clock.Advance(TimeSpan.FromMinutes(1));

        vm.Editor.Body = "words.";
        await vm.SaveNowCommand.ExecuteAsync(null);

        var after = SavedNote();
        Assert.Equal(_clock.Now, after.Modified);
        Assert.True(after.Modified > before.Modified);
        Assert.True(after.Modified > after.PublishedAt);
        Assert.Equal(before.PublishedAt, after.PublishedAt);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Reordering_attachments_moves_Modified()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        var first = Path.Combine(_home, "first.txt");
        var second = Path.Combine(_home, "second.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        _dialogs.AttachmentPaths = [first, second];
        await vm.AddAttachmentCommand.ExecuteAsync(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        var modified = SavedNote().Modified;
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(vm.MoveAttachment(vm.Attachments[1], 0));
        vm.CommitAttachmentOrder();
        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal(_clock.Now, SavedNote().Modified);
        Assert.True(SavedNote().Modified > modified);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_change_landing_during_a_save_does_not_stamp_the_note_again()
    {
        var gated = new GatedBinderStore();
        var vm = await OpenNewBinderAsync(gated);
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Body = "words";

        // The status changes while the first save is writing, so the note stays marked for the next one.
        var writing = gated.HoldNextSave();
        var firstSave = vm.SaveNowCommand.ExecuteAsync(null);
        await writing.Entered.Task;
        vm.Editor.Status = NoteStatus.Verified;
        writing.Release();
        await firstSave;
        Assert.Equal("Unsaved changes", vm.SaveStateText);
        var modified = SavedNote().Modified;
        _clock.Advance(TimeSpan.FromMinutes(1));

        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal(NoteStatus.Verified, SavedNote().Status);
        Assert.Equal(modified, SavedNote().Modified);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Undoing_to_the_old_baseline_during_a_save_keeps_the_later_edit_time()
    {
        var store = new GatedBinderStore();
        var vm = await OpenNewBinderAsync(store);
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Body = "A";
        await vm.SaveNowCommand.ExecuteAsync(null);
        _clock.Advance(TimeSpan.FromMinutes(1));
        vm.Editor.Body = "B";
        var hold = store.HoldNextSave();
        var save = vm.SaveNowCommand.ExecuteAsync(null);
        try
        {
            await hold.Entered.Task;
            _clock.Advance(TimeSpan.FromMinutes(1));
            vm.Editor.Body = "A";
        }
        finally
        {
            hold.Release();
            await save;
        }
        var editedAt = _clock.Now;
        _clock.Advance(TimeSpan.FromMinutes(1));
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal("A", SavedNote().Body);
        Assert.Equal(editedAt, SavedNote().Modified);
        await vm.ShutdownAsync();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_finishes_an_edit_entered_during_its_older_save(bool shutdown)
    {
        var store = new GatedBinderStore();
        var vm = await OpenNewBinderAsync(store);
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Body = "older";
        var hold = store.HoldNextSave();
        var close = shutdown ? vm.ShutdownAsync() : vm.CloseBinderCommand.ExecuteAsync(null);
        try
        {
            await hold.Entered.Task;
            vm.Editor.Body = "latest";
        }
        finally
        {
            hold.Release();
            await close;
        }
        Assert.Equal("latest", SavedNote().Body);
        Assert.False(vm.HasBinder);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Explicit_reload_does_not_replace_input_entered_during_its_read()
    {
        var store = new GatedBinderStore();
        var vm = await OpenNewBinderAsync(store);
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        var outside = new BinderStore().Load(BinderPath).Binder;
        outside.Notes[0].Body = "outside";
        new BinderStore().Save(BinderPath, outside);
        vm.Editor.Body = "discard authorized";
        _dialogs.ExternalChoice = ExternalChangeChoice.ReloadFromDisk;
        var hold = store.HoldNextLoad();
        var reload = vm.SaveNowCommand.ExecuteAsync(null);
        try
        {
            await hold.Entered.Task;
            vm.Editor.Body = "later input";
        }
        finally
        {
            hold.Release();
            await reload;
        }
        Assert.Equal("later input", vm.Editor.Body);
        Assert.Equal("Unsaved changes", vm.SaveStateText);
        await vm.ShutdownAsync();
    }

    private Binder SavedBinder() => new BinderStore().Load(BinderPath).Binder;

    [AvaloniaFact]
    public async Task The_binders_Modified_is_the_time_of_its_latest_content_edit()
    {
        var vm = await OpenNewBinderAsync();
        var opened = SavedBinder().Modified;

        // Adding a note is an edit of the binder's content, at the moment it was added.
        _clock.Advance(TimeSpan.FromMinutes(1));
        var added = _clock.Now;
        vm.NewNoteCommand.Execute(null);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.NotEqual(opened, added);
        Assert.Equal(added, SavedBinder().Modified);

        // A note's content edit, saved later.
        _clock.Advance(TimeSpan.FromMinutes(1));
        var edited = _clock.Now;
        vm.Editor.Body = "words";
        _clock.Advance(TimeSpan.FromMinutes(1));
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(edited, SavedBinder().Modified);

        // Deleting a note.
        _clock.Advance(TimeSpan.FromMinutes(1));
        var deleted = _clock.Now;
        _dialogs.ConfirmResult = true;
        await vm.DeleteNoteCommand.ExecuteAsync(null);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(deleted, SavedBinder().Modified);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Status_lock_and_undone_edits_leave_the_binders_Modified_alone()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        vm.Editor.Body = "words";
        await vm.SaveNowCommand.ExecuteAsync(null);
        var modified = SavedBinder().Modified;

        _clock.Advance(TimeSpan.FromMinutes(1));
        vm.Editor.Status = NoteStatus.Published;
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(modified, SavedBinder().Modified);

        _clock.Advance(TimeSpan.FromMinutes(1));
        vm.Editor.Locked = true;
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(modified, SavedBinder().Modified);

        _clock.Advance(TimeSpan.FromMinutes(1));
        vm.Editor.Locked = false;
        vm.Editor.Body = "words changed";
        vm.Editor.Body = "words";
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(modified, SavedBinder().Modified);

        // Whitespace the save's own cleanup removes.
        _clock.Advance(TimeSpan.FromMinutes(1));
        vm.Editor.Body = "words   ";
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(modified, SavedBinder().Modified);

        // A note added and deleted before the save leaves the notes as saved.
        _clock.Advance(TimeSpan.FromMinutes(1));
        vm.NewNoteCommand.Execute(null);
        _dialogs.ConfirmResult = true;
        await vm.DeleteNoteCommand.ExecuteAsync(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Equal(modified, SavedBinder().Modified);

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
        await vm.RefreshKnownBinders();
        Assert.True(missing.IsMissing);
        Assert.Empty(vm.Results);

        // The open binder's row stays quiet: its next save writes the file again.
        File.Delete(BinderPath);
        await vm.RefreshKnownBinders();
        Assert.False(open.IsMissing);

        // Removing the entry needs no file, and it does not come back.
        await vm.RemoveBinderCommand.ExecuteAsync(missing);
        Assert.DoesNotContain(vm.Binders, binder => PathKey.Equal(binder.Path, travel));
        var config = JsonSerializer.Deserialize<AppConfig>(
            File.ReadAllText(Path.Combine(_home, "config.json")), DayNoteJson.Options)!;
        Assert.DoesNotContain(config.Binders, entry => PathKey.Equal(entry.Path, travel));

        // A file that comes back clears the marker on the next look.
        File.WriteAllText(travel, "{}");
        await vm.RefreshKnownBinders();
        Assert.DoesNotContain(vm.Binders, binder => binder.IsMissing);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_later_open_wins_a_race_with_an_earlier_one_still_reading_its_file()
    {
        // B (one note) starts a background read that shares the save lock; opening C (no notes) right
        // behind it — before B's read returns — has to wait out that lock rather than read C's file at
        // the same time. Once B's read finishes, its own generation is already stale, since C's open
        // started after it: B's result must be discarded rather than briefly flashing its note into the
        // pane before C's (empty) content replaces it, and the binder that ends up open must be C, the
        // one the user opened last.
        var vm = NewViewModel();
        var pathB = Path.Combine(_home, "b.daynote");
        var pathC = Path.Combine(_home, "c.daynote");

        _dialogs.BinderToCreate = pathB;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);

        _dialogs.BinderToCreate = pathC;
        await vm.NewBinderCommand.ExecuteAsync(null);

        var itemB = Assert.Single(vm.Binders, b => PathKey.Equal(b.Path, pathB));
        var itemC = Assert.Single(vm.Binders, b => PathKey.Equal(b.Path, pathC));

        var observedNoteCounts = new List<int>();
        vm.Notes.CollectionChanged += (_, _) => observedNoteCounts.Add(vm.Notes.Count);

        var openB = vm.OpenKnownBinderCommand.ExecuteAsync(itemB);
        var openC = vm.OpenKnownBinderCommand.ExecuteAsync(itemC);
        await Task.WhenAll(openB, openC);

        Assert.DoesNotContain(1, observedNoteCounts);
        Assert.Empty(vm.Notes);
        Assert.True(itemC.IsCurrent);
        Assert.False(itemB.IsCurrent);

        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task An_open_still_reading_its_file_at_shutdown_is_discarded()
    {
        // Shutdown supersedes an open whose background read has not returned yet: when that read
        // finishes, the binder is not adopted and nothing is written after the app shut down.
        var vm = NewViewModel();
        var pathB = Path.Combine(_home, "b.daynote");
        var pathC = Path.Combine(_home, "c.daynote");

        _dialogs.BinderToCreate = pathB;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        _dialogs.BinderToCreate = pathC;
        await vm.NewBinderCommand.ExecuteAsync(null);
        var itemB = Assert.Single(vm.Binders, b => PathKey.Equal(b.Path, pathB));

        var open = vm.OpenKnownBinderCommand.ExecuteAsync(itemB);
        await vm.ShutdownAsync();
        await open;

        Assert.False(itemB.IsCurrent);
        Assert.Empty(vm.Notes);
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
        // Every message the window reports runs to a sentence or two; a short one shows the one-line case.
        var shortResult = new OperationResultViewModel(OperationResultKind.Warning, Message.Of("common.cancel"), isPersistent: true, "short");
        vm.Results.Insert(0, shortResult);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var cards = ResultCards(window);
        var oneLine = cards.Single(card => card.DataContext == shortResult);
        var wrapped = cards.Single(card => card.DataContext is OperationResultViewModel { Text: var m }
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

        await ShowBinderNotRememberedAsync(vm);
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

        await ShowBinderNotRememberedAsync(vm);
        var notRemembered = Assert.Single(vm.Results);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(host));
        Assert.Equal(notRemembered.Text, AutomationProperties.GetName(host));

        File.Delete(BinderPath);
        Directory.CreateDirectory(BinderPath);
        vm.Editor.Title = "Unsaved title";
        await vm.SaveNowCommand.ExecuteAsync(null);
        var failure = vm.Results[0];
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(host));
        Assert.Equal(failure.Text, AutomationProperties.GetName(host));

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

    /// <summary>Counts rows the backup history holds for <paramref name="path"/>, reading the store
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
        _windows.Add(window);
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
        _windows.Add(window);
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
        // First, since a successful open settles the picker failures below.
        await ShowBinderNotRememberedAsync(vm);

        var pickerFailure = new IOException("picker unavailable");
        _dialogs.OpenBinderPickerError = pickerFailure;
        await vm.OpenBinderCommand.ExecuteAsync(null);
        _dialogs.NewBinderPickerError = pickerFailure;
        await vm.NewBinderCommand.ExecuteAsync(null);
        _dialogs.OpenBinderPickerError = null;
        _dialogs.NewBinderPickerError = null;

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
    /// Opens a binder DayNote does not know yet while its settings file cannot be written, so the binder
    /// opens but is not remembered: a warning. The test's own binder is open again afterwards, and the
    /// settings file can be written again.
    /// </summary>
    private async Task ShowBinderNotRememberedAsync(MainWindowViewModel vm)
    {
        var unremembered = Path.Combine(_home, "unremembered.daynote");
        var now = DateTimeOffset.UtcNow;
        new BinderStore().Save(unremembered, new Binder { Id = IdGenerator.New(), Created = now, Modified = now });
        var configFile = Path.Combine(_home, "config.json");
        File.Delete(configFile);
        Directory.CreateDirectory(configFile);
        _dialogs.BinderToOpen = unremembered;
        await vm.OpenBinderCommand.ExecuteAsync(null);
        Directory.Delete(configFile);
        await vm.OpenKnownBinderCommand.ExecuteAsync(vm.Binders.Single(binder => PathKey.Equal(binder.Path, BinderPath)));
        Assert.True(PathKey.Equal(vm.Binders.Single(binder => binder.IsCurrent).Path, BinderPath));
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
        // A test that failed midway may have left a window open, a view model with a pending autosave or a
        // quit to make, and a save, load or copy held on a background thread. Every step runs whatever the
        // others did, and their failures are reported together, so none of it runs into the next test. The
        // temp root goes either way: the failure's own message is the evidence (tests-folder-conventions).
        var failures = new List<Exception>();
        void Attempt(Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            foreach (var vm in _viewModels)
            {
                Attempt(vm.BeginShutdown);
            }

            foreach (var window in _windows.Where(window => window.IsVisible))
            {
                Attempt(() =>
                {
                    window.DataContext = null;
                    window.Close();
                });
            }

            Attempt(() => Dispatcher.UIThread.RunJobs());
        }

        Attempt(GatedBinderStore.Hold.ReleaseAll);
        Attempt(HeldCopy.ReleaseAll);

        // Close the backup store so its singleton re-opens against the next test's throwaway root and
        // releases the file handle before the directory is deleted.
        Attempt(BackupStore.Close);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp directory is harmless.
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Test teardown failed.", failures);
        }
    }

    // ----- Attachments being added (developer decision: finished before a switch, close or quit) -----

    private async Task<(MainWindowViewModel Vm, HeldCopy Copy, Note Note, string File, Task Adding)> StartHeldAddAsync()
    {
        var held = new HeldCopy();
        var vm = NewViewModel(copyFile: held.Copy);
        _dialogs.BinderToCreate = BinderPath;
        await vm.NewBinderCommand.ExecuteAsync(null);
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        var note = vm.SelectedNote!.Note;
        var file = Path.Combine(_home, "video.bin");
        File.WriteAllText(file, "a large video");

        var adding = vm.AddDroppedFiles([file]);
        await held.Entered.Task;
        return (vm, held, note, file, adding);
    }

    private string[] AssetFiles(Note note)
    {
        var directory = BinderStore.NoteAssetsDirectory(BinderPath, note.Id);
        return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
    }

    [AvaloniaFact]
    public async Task An_add_that_finishes_after_another_note_is_selected_stays_with_its_own_note()
    {
        var (vm, held, noteA, _, adding) = await StartHeldAddAsync();
        vm.NewNoteCommand.Execute(null);
        var noteB = vm.SelectedNote!.Note;

        held.Release();
        await adding;

        // The file is A's; B's pane keeps showing B's own (empty) list.
        Assert.Single(noteA.Attachments);
        Assert.Empty(noteB.Attachments);
        Assert.Empty(vm.Attachments);
        Assert.Null(vm.AttachmentResult);

        // A row of A's list cannot remove anything while B is selected.
        vm.SelectedNote = vm.Notes.Single(item => ReferenceEquals(item.Note, noteA));
        var rowOfA = Assert.Single(vm.Attachments);
        vm.SelectedNote = vm.Notes.Single(item => ReferenceEquals(item.Note, noteB));
        await vm.RemoveAttachmentCommand.ExecuteAsync(rowOfA);

        Assert.Single(noteA.Attachments);
        Assert.True(File.Exists(rowOfA.FullPath));
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_binder_switch_waits_for_an_add_and_saves_it_with_the_binder()
    {
        var (vm, held, note, _, adding) = await StartHeldAddAsync();
        _dialogs.BinderToCreate = Path.Combine(_home, "second.daynote");

        var switching = vm.NewBinderCommand.ExecuteAsync(null);
        _clock.Advance(MainWindowViewModel.ImportWaitNoticeDelay);
        Dispatcher.UIThread.RunJobs();

        // Still waiting, and saying so.
        Assert.False(switching.IsCompleted);
        Assert.Equal(1, _dialogs.AttachmentWaits);

        held.Release();
        await switching;
        await adding;

        var saved = new BinderStore().Load(BinderPath).Binder.Notes.Single();
        var name = Assert.Single(saved.Attachments);
        Assert.Equal(Path.Combine(BinderStore.NoteAssetsDirectory(BinderPath, note.Id), name), Assert.Single(AssetFiles(note)));
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task An_add_that_settles_before_the_notice_delay_shows_no_wait()
    {
        var (vm, held, _, _, adding) = await StartHeldAddAsync();
        _dialogs.BinderToCreate = Path.Combine(_home, "second.daynote");

        var switching = vm.NewBinderCommand.ExecuteAsync(null);
        held.Release();
        await switching;
        await adding;

        Assert.Equal(0, _dialogs.AttachmentWaits);
        Assert.Single(new BinderStore().Load(BinderPath).Binder.Notes.Single().Attachments);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_quit_waits_for_an_add_and_saves_it()
    {
        var (vm, held, _, _, adding) = await StartHeldAddAsync();

        var quitting = vm.QuitAsync();
        Assert.False(quitting.IsCompleted);
        held.Release();

        Assert.True(await quitting);
        await adding;
        Assert.Single(new BinderStore().Load(BinderPath).Binder.Notes.Single().Attachments);
    }

    [AvaloniaFact]
    public async Task Stopping_the_wait_switches_at_once_and_removes_the_copy_when_it_lands()
    {
        var (vm, held, note, file, adding) = await StartHeldAddAsync();
        _dialogs.AttachmentWaitAnswer = _ => Task.FromResult(true);
        var second = Path.Combine(_home, "second.daynote");
        _dialogs.BinderToCreate = second;

        var switching = vm.NewBinderCommand.ExecuteAsync(null);
        _clock.Advance(MainWindowViewModel.ImportWaitNoticeDelay);
        await switching;

        // The switch went ahead without the file, and says which file was not added.
        Assert.False(adding.IsCompleted);
        Assert.Equal(second, vm.SelectedBinder?.Path);
        var result = Assert.Single(vm.Results);
        Assert.Equal(OperationResultKind.Warning, result.Kind);
        Assert.Contains(Path.GetFileName(file), result.Text, StringComparison.Ordinal);
        Assert.Empty(new BinderStore().Load(BinderPath).Binder.Notes.Single().Attachments);

        // The copy that lands afterwards is removed, and nothing refers to it.
        held.Release();
        await adding;
        Assert.Empty(AssetFiles(note));
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task An_ending_session_does_not_wait_for_an_add_and_its_late_copy_is_removed()
    {
        var (vm, held, note, _, adding) = await StartHeldAddAsync();

        await vm.EndSessionAsync();

        Assert.Equal(0, _dialogs.AttachmentWaits);
        Assert.False(adding.IsCompleted);
        held.Release();
        await adding;
        Assert.Empty(AssetFiles(note));
        Assert.Empty(new BinderStore().Load(BinderPath).Binder.Notes.Single().Attachments);
    }

    [AvaloniaFact]
    public async Task Deleting_a_note_while_files_are_added_to_it_leaves_no_files_behind()
    {
        var (vm, held, note, _, adding) = await StartHeldAddAsync();

        var deleting = vm.DeleteNoteCommand.ExecuteAsync(null);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        // The note's folder goes only once the copy still running has finished.
        Assert.False(deleting.IsCompleted);
        held.Release();
        await deleting;
        await adding;

        Assert.Empty(new BinderStore().Load(BinderPath).Binder.Notes);
        Assert.False(Directory.Exists(BinderStore.NoteAssetsDirectory(BinderPath, note.Id)));
        Assert.Empty(vm.Results);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task An_outside_change_question_closed_unanswered_writes_nothing_and_keeps_the_edit()
    {
        var vm = await OpenNewBinderAsync();
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        ChangeOutside("Changed outside");
        var outside = File.ReadAllBytes(BinderPath);
        _dialogs.ExternalChoice = ExternalChangeChoice.Unanswered;

        vm.Editor.Body = "local edit";
        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal(outside, File.ReadAllBytes(BinderPath));
        Assert.Equal("local edit", vm.Editor.Body);
        Assert.Equal("Unsaved changes", vm.SaveStateText);
        vm.BeginShutdown();
    }

    // ----- A required question that cannot be shown (modal-dialog conventions) ----------------------

    [AvaloniaFact]
    public async Task A_quit_question_that_cannot_be_shown_cancels_the_quit_and_keeps_the_edit()
    {
        var (vm, store, log) = await OpenWithUnsavedEditAsync("kept");
        store.SaveFailure = new IOException("No space left on device");
        _dialogs.QuitAnswer = () => throw new InvalidOperationException("The owner window is gone.");

        Assert.False(await vm.QuitAsync());

        Assert.True(vm.HasBinder);
        Assert.Equal("kept", vm.Editor.Body);
        Assert.Contains(("error", "Could not show the quit question; the quit is cancelled"), log.Entries);

        store.SaveFailure = null;
        Assert.True(await vm.QuitAsync());
        Assert.Equal("kept", SavedBody());
    }

    [AvaloniaFact]
    public async Task A_wait_for_attachments_that_cannot_be_shown_cancels_the_quit_and_the_add_still_lands()
    {
        var (vm, held, note, _, adding) = await StartHeldAddAsync();
        _dialogs.AttachmentWaitAnswer = _ => throw new InvalidOperationException("The owner window is gone.");
        vm.BeginShutdown();

        var quitting = vm.QuitAsync();
        _clock.Advance(MainWindowViewModel.ImportWaitNoticeDelay);
        Assert.False(await quitting);

        held.Release();
        await adding;
        Assert.Single(note.Attachments);
        Assert.True(vm.HasBinder);
        await vm.SaveNowCommand.ExecuteAsync(null);
        Assert.Single(new BinderStore().Load(BinderPath).Binder.Notes.Single().Attachments);
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task A_wait_for_attachments_that_cannot_be_shown_keeps_the_binder_open_instead_of_switching()
    {
        var (vm, held, _, _, adding) = await StartHeldAddAsync();
        _dialogs.AttachmentWaitAnswer = _ => throw new InvalidOperationException("The owner window is gone.");
        _dialogs.BinderToCreate = Path.Combine(_home, "second.daynote");

        var switching = vm.NewBinderCommand.ExecuteAsync(null);
        _clock.Advance(MainWindowViewModel.ImportWaitNoticeDelay);
        await switching;

        Assert.Equal(BinderPath, vm.SelectedBinder?.Path);
        held.Release();
        await adding;
        await vm.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task An_outside_change_question_that_cannot_be_shown_writes_nothing()
    {
        var log = new ListLogger();
        var vm = await OpenNewBinderAsync(log: log);
        vm.NewNoteCommand.Execute(null);
        await vm.SaveNowCommand.ExecuteAsync(null);
        ChangeOutside("Changed outside");
        var outside = File.ReadAllBytes(BinderPath);
        _dialogs.ExternalAnswer = () => throw new InvalidOperationException("The owner window is gone.");

        vm.Editor.Body = "local edit";
        await vm.SaveNowCommand.ExecuteAsync(null);

        Assert.Equal(outside, File.ReadAllBytes(BinderPath));
        Assert.Equal("local edit", vm.Editor.Body);
        Assert.Contains(("error", "Could not show the question about a binder changed on disk"), log.Entries);
        vm.BeginShutdown();
    }

    [AvaloniaFact]
    public async Task A_startup_message_that_cannot_be_shown_is_logged_instead_of_lost()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(ConfigPath, "{ not json");
        _dialogs.ErrorDialogFailure = new InvalidOperationException("The owner window is gone.");
        var log = new RecordingLogger();
        var vm = new MainWindowViewModel(new AppPaths(), _dialogs, log);
        _viewModels.Add(vm);

        await vm.InitializeAsync();

        Assert.False(vm.IsReady);
        Assert.Contains(("error", "Startup could not finish"), log.Entries);
    }

    private sealed class FakeDialogService : IDialogService
    {
        public string? BinderToCreate { get; set; }
        public string? BinderToOpen { get; set; }
        public IReadOnlyList<string> AttachmentPaths { get; set; } = Array.Empty<string>();
        public bool ConfirmResult { get; set; } = true;
        public ExternalChangeChoice ExternalChoice { get; set; } = ExternalChangeChoice.KeepMine;
        public bool SettingsApplied { get; set; }
        public Action<AppConfig>? SettingsEdit { get; set; }
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
        public Message? LastConfirmMessage { get; private set; }
        public Task<bool> ConfirmAsync(Message title, Message message, string confirmLabelKey, bool destructive = false)
        {
            LastConfirmMessage = message;
            return Task.FromResult(ConfirmResult);
        }
        public List<(Message Title, Message Message)> Errors { get; } = [];
        /// <summary>When set, the error dialog cannot be shown and fails with it.</summary>
        public Exception? ErrorDialogFailure { get; set; }
        public Task ShowErrorAsync(Message title, Message message)
        {
            Errors.Add((title, message));
            return ErrorDialogFailure is null ? Task.CompletedTask : Task.FromException(ErrorDialogFailure);
        }
        public Task ShowAboutAsync() => Task.CompletedTask;
        public Task ShowShortcutsAsync() => Task.CompletedTask;
        public Task<bool> ShowSettingsAsync(AppConfig config, Func<AppConfig, bool> trySave)
        {
            SettingsEdit?.Invoke(config);
            return Task.FromResult(SettingsApplied && trySave(config));
        }
        public int ExternalChangeQuestions { get; private set; }
        /// <summary>When set, answers the question instead of <see cref="ExternalChoice"/>, possibly later.</summary>
        public Func<Task<ExternalChangeChoice>>? ExternalAnswer { get; set; }
        public Task<ExternalChangeChoice> AskExternalChangeAsync(string binderName)
        {
            ExternalChangeQuestions++;
            return ExternalAnswer?.Invoke() ?? Task.FromResult(ExternalChoice);
        }
        /// <summary>The binder named by each quit question, in order.</summary>
        public List<string> QuitQuestions { get; } = [];

        /// <summary>How each quit question is answered; it may also change the world before answering.</summary>
        public Func<UnsavedQuitChoice> QuitAnswer { get; set; } = () => UnsavedQuitChoice.Stay;

        public Task<UnsavedQuitChoice> AskQuitWithUnsavedBinderAsync(string binderName)
        {
            QuitQuestions.Add(binderName);
            return Task.FromResult(QuitAnswer());
        }

        /// <summary>How many times a close or quit showed that it was waiting for attachments.</summary>
        public int AttachmentWaits { get; private set; }

        /// <summary>
        /// How the wait is answered: by default it lasts until the adds settle. A test that stops waiting
        /// returns true from here, possibly later.
        /// </summary>
        public Func<Task, Task<bool>>? AttachmentWaitAnswer { get; set; }

        public async Task<bool> WaitForAttachmentsAsync(Task settled, TimeSpan stopOfferedAfter)
        {
            AttachmentWaits++;
            if (AttachmentWaitAnswer is { } answer)
            {
                return await answer(settled);
            }

            await settled;
            return false;
        }

        public Task OpenPathExternallyAsync(string path)
        {
            LastOpenedPath = path;
            return OpenPathError is null
                ? Task.CompletedTask
                : Task.FromException(OpenPathError);
        }
    }

    /// <summary>
    /// A binder store whose next write or load, once armed, stops at its file I/O until the test releases
    /// it, so a competing action provably lands while that I/O is in flight.
    /// </summary>
    private sealed class GatedBinderStore : BinderStore
    {
        private Hold? _nextSave;
        private Hold? _nextLoad;

        public Hold HoldNextSave() => _nextSave = new Hold();

        public Hold HoldNextLoad() => _nextLoad = new Hold();

        public override LoadedBinder Load(string path)
        {
            Interlocked.Exchange(ref _nextLoad, null)?.Wait();
            return base.Load(path);
        }

        /// <summary>While set, every write fails with it, as a full disk or a vanished volume would.</summary>
        public Exception? SaveFailure { get; set; }

        public override SavedBinder SaveText(string path, string text, string? expectedHash)
        {
            Interlocked.Exchange(ref _nextSave, null)?.Wait();
            if (SaveFailure is { } failure)
            {
                throw failure;
            }

            return base.SaveText(path, text, expectedHash);
        }

        public sealed class Hold
        {
            private static readonly List<Hold> Unreleased = [];
            private readonly ManualResetEventSlim _released = new();

            public Hold()
            {
                lock (Unreleased)
                {
                    Unreleased.Add(this);
                }
            }

            /// <summary>Releases every hold a test left, so a failed test never strands its thread.</summary>
            public static void ReleaseAll()
            {
                lock (Unreleased)
                {
                    Unreleased.ForEach(hold => hold.Release());
                    Unreleased.Clear();
                }
            }

            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Release() => _released.Set();

            internal void Wait()
            {
                Entered.SetResult();
                _released.Wait();
            }
        }
    }

    /// <summary>An attachment copy that stops before it starts until the test releases it, as a large file or
    /// a slow drive would; then it copies as the app does.</summary>
    private sealed class HeldCopy
    {
        private static readonly List<HeldCopy> Unreleased = [];
        private readonly ManualResetEventSlim _released = new();

        public HeldCopy()
        {
            lock (Unreleased)
            {
                Unreleased.Add(this);
            }
        }

        public static void ReleaseAll()
        {
            lock (Unreleased)
            {
                Unreleased.ForEach(held => held.Release());
                Unreleased.Clear();
            }
        }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _released.Set();

        public void Copy(string source, string destination)
        {
            Entered.TrySetResult();
            _released.Wait();
            AtomicFile.CopyNew(source, destination);
        }
    }

    /// <summary>Keeps what was logged, so a test can see that a failure the user is not shown is recorded.</summary>
    private sealed class ListLogger : IAppLogger
    {
        public List<(string Level, string Message)> Entries { get; } = [];

        public void Debug(string message, object? data = null, Exception? error = null) => Entries.Add(("debug", message));
        public void Info(string message, object? data = null, Exception? error = null) => Entries.Add(("info", message));
        public void Warn(string message, object? data = null, Exception? error = null) => Entries.Add(("warn", message));
        public void Error(string message, object? data = null, Exception? error = null) => Entries.Add(("error", message));
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Debug(string message, object? data = null, Exception? error = null) { }
        public void Info(string message, object? data = null, Exception? error = null) { }
        public void Warn(string message, object? data = null, Exception? error = null) { }
        public void Error(string message, object? data = null, Exception? error = null) { }
    }
}
