using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DayNote.Core.Configuration;
using DayNote.Core.Identity;
using DayNote.Core.Models;
using DayNote.Core.Storage;
using DayNote.Core.Toml;
using DayNote.Desktop.Services;
using DayNote.Desktop.State;
using Serilog;

namespace DayNote.Desktop.ViewModels;

/// <summary>
/// Orchestrates the main window: the four panes (recent notebooks, notes, editor, attachments),
/// load-gated configuration and state, opening and closing notebooks with per-notebook locking,
/// autosave with per-note dirty tracking, backups on save and close, external-modification
/// detection, and the recent-notebooks list. Side-effecting file and database work is delegated to
/// the Core storage layer; dialogs and the native file picker go through <see cref="IDialogService"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppPaths _paths;
    private readonly NotebookStore _notebooks = new();
    private readonly JsonStore<AppConfig> _configStore;
    private readonly JsonStore<AppState> _stateStore;
    private readonly IDialogService _dialogs;
    private readonly ILogger _log;

    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _externalTimer;

    private readonly List<NoteListItemViewModel> _allNotes = new();
    private readonly List<RecentNotebookItemViewModel> _allRecents = new();
    private readonly HashSet<string> _dirtyNoteIds = new();

    private BackupStore? _backups;
    private AppConfig _config = new();
    private AppState _state = new();
    private Exception? _loadError;

    private LoadedNotebook? _current;
    private NotebookLock? _lock;
    private string _baselineHash = string.Empty;
    private bool _dirty;
    private bool _externalChangeAcknowledged;
    private bool _externalCheckInProgress;
    private bool _savingInProgress;
    private SaveState _saveState = SaveState.Saved;

    public MainWindowViewModel(AppPaths paths, IDialogService dialogs, ILogger log)
    {
        _paths = paths;
        _dialogs = dialogs;
        _log = log;
        _configStore = new JsonStore<AppConfig>(paths.ConfigFile);
        _stateStore = new JsonStore<AppState>(paths.StateFile);

        // All startup I/O (directory creation, opening the backup database, reading config/state)
        // is gated here: any failure becomes _loadError, which disables saving and surfaces an
        // error dialog once the window is shown, rather than crashing before any UI exists.
        LoadConfigAndState();

        Editor = new EditorViewModel(_config.DisplayTimeZone);
        Editor.Edited += OnEditorEdited;
        DataDirectory = _paths.Root;

        _autosaveTimer = new DispatcherTimer();
        _autosaveTimer.Tick += async (_, _) =>
        {
            _autosaveTimer.Stop();
            await SaveCurrentAsync(force: false);
            if (_dirty)
            {
                // The save failed or was deferred (e.g. while an external-change conflict dialog
                // is open); reschedule so the edits are retried rather than stranded.
                _autosaveTimer.Start();
            }
        };

        _externalTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _externalTimer.Tick += async (_, _) => await CheckExternalChangeAsync();

        if (_loadError is null)
        {
            ApplyConfig();
            RestorePaneWidths();
            RebuildRecents();
            IsReady = true;
        }

        UpdateSaveStateText();
    }

    public EditorViewModel Editor { get; }

    public ObservableCollection<NoteListItemViewModel> Notes { get; } = new();
    public ObservableCollection<RecentNotebookItemViewModel> RecentNotebooks { get; } = new();
    public ObservableCollection<AttachmentItemViewModel> Attachments { get; } = new();

    // Initial window geometry for the view to apply before the window is shown.
    public double InitialWindowWidth => _state.WindowWidth;
    public double InitialWindowHeight => _state.WindowHeight;
    public double? InitialWindowX => _state.WindowX;
    public double? InitialWindowY => _state.WindowY;
    public bool InitialWindowMaximized => _state.WindowMaximized;

    [ObservableProperty]
    private bool _isReady;

    [ObservableProperty]
    private string _saveStateText = "Saved";

    [ObservableProperty]
    private bool _hasNotebook;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private string _notebookTitle = "DayNote";

    [ObservableProperty]
    private string _notebookPath = string.Empty;

    [ObservableProperty]
    private string _dataDirectory = string.Empty;

    [ObservableProperty]
    private string _notesFilter = string.Empty;

    [ObservableProperty]
    private string _recentFilter = string.Empty;

    [ObservableProperty]
    private NoteListItemViewModel? _selectedNote;

    [ObservableProperty]
    private double _recentPaneWidth = 220;

    [ObservableProperty]
    private double _notesPaneWidth = 260;

    [ObservableProperty]
    private double _attachmentsPaneWidth = 260;

    [ObservableProperty]
    private FontFamily _editorFontFamily = new("Inter");

    [ObservableProperty]
    private double _editorFontSize = 14;

    // ----- Lifecycle -----------------------------------------------------------------------------

    /// <summary>Runs after the window is shown, so dialogs have an owner.</summary>
    public async Task InitializeAsync()
    {
        if (_loadError is not null)
        {
            await _dialogs.ShowErrorAsync(
                "Load failed",
                "DayNote could not read its configuration or state files, so saving is disabled to avoid " +
                "overwriting good data. Fix or remove the files in " + _paths.Root + " and restart.\n\n" +
                _loadError.Message);
            return;
        }

        if (!string.IsNullOrEmpty(_state.CurrentNotebookPath) && File.Exists(_state.CurrentNotebookPath))
        {
            await OpenNotebookPathAsync(_state.CurrentNotebookPath!, isNew: false, selectNoteId: _state.CurrentNoteId);
        }

        _externalTimer.Start();
    }

    /// <summary>Flushes pending work and releases the lock on shutdown. Geometry is captured first by the view.</summary>
    public async Task ShutdownAsync()
    {
        _externalTimer.Stop();
        _autosaveTimer.Stop();

        // Persist state (including the current note id) while the notebook is still open;
        // CloseCurrentAsync clears the selection, which would otherwise null out CurrentNoteId.
        PersistState();
        await CloseCurrentAsync(clearSelection: false);
    }

    public void CaptureWindowGeometry(double width, double height, double x, double y, bool maximized)
    {
        _state.WindowMaximized = maximized;

        // While maximized, width/height/position are the maximized bounds, not the restore bounds,
        // so they are not stored; the last non-maximized bounds (or defaults) remain the restore size.
        if (!maximized)
        {
            _state.WindowWidth = width;
            _state.WindowHeight = height;
            _state.WindowX = x;
            _state.WindowY = y;
        }
    }

    // ----- Commands ------------------------------------------------------------------------------

    [RelayCommand]
    private async Task NewNotebook()
    {
        if (!IsReady)
        {
            return;
        }

        var path = await _dialogs.PickNotebookToCreateAsync();
        if (path is null)
        {
            return;
        }

        await OpenNotebookPathAsync(EnsureDaynoteExtension(path), isNew: true);
    }

    [RelayCommand]
    private async Task OpenNotebook()
    {
        if (!IsReady)
        {
            return;
        }

        var path = await _dialogs.PickNotebookToOpenAsync();
        if (path is not null)
        {
            await OpenNotebookPathAsync(path, isNew: false);
        }
    }

    [RelayCommand]
    private async Task OpenRecent(RecentNotebookItemViewModel item)
    {
        if (!File.Exists(item.Path))
        {
            item.IsMissing = true;
            ShowToast(ToastKind.Warning, "That notebook is no longer at " + item.Path);
            return;
        }

        await OpenNotebookPathAsync(item.Path, isNew: false);
    }

    [RelayCommand]
    private async Task CloseNotebook() => await CloseCurrentAsync(clearSelection: true);

    [RelayCommand]
    private void NewNote()
    {
        if (!IsReady || _current is null || IsReadOnly)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var note = new Note
        {
            Id = IdGenerator.NewUnique(_current.Notebook.Notes.Select(n => n.Id)),
            Title = string.Empty,
            Created = now,
            Modified = now,
            Body = string.Empty,
        };

        _current.Notebook.Notes.Add(note);
        _allNotes.Add(new NoteListItemViewModel(note, _config.DisplayTimeZone));
        NotesFilter = string.Empty;
        RebuildNotes();
        SelectedNote = Notes.FirstOrDefault(n => n.Note.Id == note.Id);
        MarkDirty(note.Id);
    }

    [RelayCommand]
    private async Task DeleteNote()
    {
        if (!IsReady || _current is null || IsReadOnly || SelectedNote is null)
        {
            return;
        }

        var note = SelectedNote.Note;
        var label = string.IsNullOrWhiteSpace(note.Title) ? "untitled" : note.Title;
        if (!await _dialogs.ConfirmAsync("Delete note", $"Delete “{label}”? Its attachment files are left on disk."))
        {
            return;
        }

        _current.Notebook.Notes.Remove(note);
        _allNotes.RemoveAll(n => ReferenceEquals(n.Note, note));
        RebuildNotes();
        SelectedNote = Notes.FirstOrDefault();
        MarkDirty(noteId: null);
    }

    [RelayCommand]
    private async Task AddAttachment()
    {
        if (!IsReady || _current is null || IsReadOnly || SelectedNote is null)
        {
            return;
        }

        var note = SelectedNote.Note;
        var files = await _dialogs.PickAttachmentsAsync();
        if (files.Count == 0 || !IsLiveNote(note))
        {
            return;
        }

        var directory = NotebookStore.NoteAssetsDirectory(_current.Path, note.Id);
        Directory.CreateDirectory(directory);

        foreach (var source in files)
        {
            try
            {
                var name = UniqueFileName(directory, Path.GetFileName(source));
                File.Copy(source, Path.Combine(directory, name));
                note.Attachments.Add(name);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to add attachment {Source}", source);
                ShowToast(ToastKind.Error, "Could not add " + Path.GetFileName(source));
            }
        }

        LoadAttachments(note);
        MarkDirty(note.Id);
    }

    [RelayCommand]
    private async Task RemoveAttachment(AttachmentItemViewModel item)
    {
        if (!IsReady || _current is null || IsReadOnly || SelectedNote is null)
        {
            return;
        }

        var note = SelectedNote.Note;
        if (!await _dialogs.ConfirmAsync("Remove attachment", $"Remove “{item.FileName}”? The file will be deleted."))
        {
            return;
        }

        // The notebook may have been reloaded (replacing note objects) while the dialog was open;
        // if the captured note is no longer live, abort rather than delete a file out from under it.
        if (!IsLiveNote(note))
        {
            return;
        }

        note.Attachments.Remove(item.FileName);
        try
        {
            if (File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to delete attachment {Path}", item.FullPath);
        }

        LoadAttachments(note);
        MarkDirty(note.Id);
    }

    [RelayCommand]
    private async Task OpenAttachment(AttachmentItemViewModel item)
    {
        if (File.Exists(item.FullPath))
        {
            await _dialogs.OpenPathExternallyAsync(item.FullPath);
        }
    }

    [RelayCommand]
    private async Task RestoreBackup()
    {
        if (!IsReady || _current is null || _backups is null)
        {
            return;
        }

        var versions = _backups.List(PathKey.Normalize(_current.Path));
        if (versions.Count == 0)
        {
            ShowToast(ToastKind.Info, "No backups for this notebook yet.");
            return;
        }

        var chosen = await _dialogs.PickBackupVersionAsync(versions, _config.DisplayTimeZone);
        if (chosen is null)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync("Restore backup", "Replace the current notebook with the selected backup? Unsaved edits will be lost."))
        {
            return;
        }

        var content = _backups.GetContent(chosen.Id);
        if (content is null)
        {
            await _dialogs.ShowErrorAsync("Restore failed", "That backup version no longer exists.");
            return;
        }

        try
        {
            _notebooks.WriteRaw(_current.Path, content);
            AdoptLoaded(_notebooks.Load(_current.Path), SelectedNote?.Note.Id);
            ShowToast(ToastKind.Info, "Backup restored.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to restore backup for {Path}", _current.Path);
            await _dialogs.ShowErrorAsync("Restore failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        if (!IsReady)
        {
            return;
        }

        var working = _config.Copy();
        if (!await _dialogs.ShowSettingsAsync(working))
        {
            return;
        }

        _config = working;
        try
        {
            _configStore.Save(_config);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save settings");
            ShowToast(ToastKind.Error, "Could not save settings.");
        }

        ApplyConfig();
        foreach (var note in _allNotes)
        {
            note.Refresh();
        }

        Editor.RefreshMetadata();
    }

    [RelayCommand]
    private async Task OpenShortcuts() => await _dialogs.ShowShortcutsAsync();

    [RelayCommand]
    private async Task OpenAbout() => await _dialogs.ShowAboutAsync();

    [RelayCommand]
    private async Task SaveNow() => await SaveCurrentAsync(force: false);

    [RelayCommand]
    private void CycleFont()
    {
        if (_config.EditorFonts.Count == 0)
        {
            return;
        }

        var index = _config.EditorFonts.FindIndex(f => string.Equals(f, _config.EditorFont, StringComparison.OrdinalIgnoreCase));
        var next = _config.EditorFonts[(index + 1) % _config.EditorFonts.Count];
        _config.EditorFont = next;
        EditorFontFamily = new FontFamily(next);
        if (IsReady)
        {
            try
            {
                _configStore.Save(_config);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to save font preference");
            }
        }

        ShowToast(ToastKind.Info, "Font: " + next);
    }

    // ----- Notebook open / close / save ----------------------------------------------------------

    private async Task OpenNotebookPathAsync(string path, bool isNew, string? selectNoteId = null)
    {
        if (!IsReady)
        {
            return;
        }

        await CloseCurrentAsync(clearSelection: false);

        var acquired = NotebookLock.TryAcquire(_paths, path);
        var readOnly = false;
        if (acquired is null)
        {
            var choice = await _dialogs.AskLockedNotebookAsync(Path.GetFileNameWithoutExtension(path));
            if (choice == LockedNotebookChoice.Cancel)
            {
                return;
            }

            readOnly = true;
        }

        LoadedNotebook loaded;
        try
        {
            if (isNew)
            {
                var notebook = new Notebook
                {
                    Id = IdGenerator.New(),
                    Title = Path.GetFileNameWithoutExtension(path),
                    Created = DateTimeOffset.UtcNow,
                    Modified = DateTimeOffset.UtcNow,
                };
                var saved = _notebooks.Save(path, notebook);
                loaded = new LoadedNotebook(notebook, saved.Path, saved.ContentHash);
            }
            else
            {
                loaded = _notebooks.Load(path);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to open notebook {Path}", path);
            acquired?.Dispose();
            await _dialogs.ShowErrorAsync("Could not open notebook", ex.Message);
            return;
        }

        _lock = acquired;
        IsReadOnly = readOnly;
        AdoptLoaded(loaded, selectNoteId);
        NotebookPath = loaded.Path;

        AddRecent(loaded.Path);
        _state.CurrentNotebookPath = loaded.Path;
        PersistState();

        if (readOnly)
        {
            ShowToast(ToastKind.Warning, "Opened read-only — the notebook is in use by another instance.");
        }
    }

    private async Task CloseCurrentAsync(bool clearSelection)
    {
        if (_current is null)
        {
            return;
        }

        _autosaveTimer.Stop();
        if (!IsReadOnly)
        {
            if (_dirty)
            {
                // Flush pending edits and record a forced backup of the just-serialized text in a
                // single pass (SaveCurrentAsync with force:true backs up the text it wrote).
                await SaveCurrentAsync(force: true);
            }
            else
            {
                // No unsaved edits: capture the current content as a backup once on close.
                RecordCloseBackup();
            }
        }

        _lock?.Dispose();
        _lock = null;
        _current = null;
        IsReadOnly = false;
        HasNotebook = false;
        _dirty = false;
        _dirtyNoteIds.Clear();
        Editor.Load(null);
        _allNotes.Clear();
        Notes.Clear();
        DisposeAttachments();
        Attachments.Clear();
        SelectedNote = null;
        NotebookTitle = "DayNote";
        NotebookPath = string.Empty;
        SetSaveState(SaveState.Saved);

        if (clearSelection)
        {
            _state.CurrentNotebookPath = null;
            _state.CurrentNoteId = null;
            PersistState();
        }
    }

    private async Task SaveCurrentAsync(bool force)
    {
        if (!IsReady || _current is null || IsReadOnly || _externalCheckInProgress)
        {
            // Do not save while an external-change conflict is being resolved; the autosave Tick
            // reschedules so the edits are not lost.
            return;
        }

        if (!_dirty && !force)
        {
            return;
        }

        _savingInProgress = true;
        try
        {
            SetSaveState(SaveState.Saving);
            var now = DateTimeOffset.UtcNow;
            var notebook = _current.Notebook;

            foreach (var id in _dirtyNoteIds)
            {
                var note = notebook.Notes.FirstOrDefault(n => n.Id == id);
                if (note is not null)
                {
                    note.Modified = now;
                }
            }

            if (_dirty)
            {
                notebook.Modified = now;
            }

            var saved = _notebooks.Save(_current.Path, notebook);
            _baselineHash = saved.ContentHash;
            _externalChangeAcknowledged = false;
            _dirty = false;
            _dirtyNoteIds.Clear();

            RecordBackup(saved.Text, force);
            Editor.RefreshMetadata();
            RefreshSelectedListItem();
            SetSaveState(SaveState.Saved);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save notebook {Path}", _current?.Path);
            SetSaveState(SaveState.Error);
            ShowToast(ToastKind.Error, "Save failed: " + ex.Message);
        }
        finally
        {
            _savingInProgress = false;
        }
    }

    private void RecordBackup(string content, bool force)
    {
        if (_current is null || _backups is null)
        {
            return;
        }

        try
        {
            _backups.Record(
                PathKey.Normalize(_current.Path),
                content,
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(Math.Max(1, _config.BackupThrottleSeconds)),
                force);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to record backup");
        }
    }

    private void RecordCloseBackup()
    {
        if (_current is null || _backups is null)
        {
            return;
        }

        try
        {
            var text = NotebookTomlWriter.Write(_current.Notebook);
            _backups.Record(PathKey.Normalize(_current.Path), text, DateTimeOffset.UtcNow, TimeSpan.Zero, force: true);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to record close backup");
        }
    }

    private async Task CheckExternalChangeAsync()
    {
        if (!IsReady || _current is null || IsReadOnly || _externalChangeAcknowledged
            || _externalCheckInProgress || _savingInProgress)
        {
            return;
        }

        _externalCheckInProgress = true;
        try
        {
            var change = _notebooks.CheckExternalChange(_current.Path, _baselineHash);
            switch (change)
            {
                case ExternalChange.None:
                    return;

                case ExternalChange.Deleted:
                    _externalChangeAcknowledged = true;
                    ShowToast(ToastKind.Warning, "The notebook file was deleted. Your edits remain; saving will recreate it.");
                    return;

                case ExternalChange.Modified when !_dirty:
                    ReloadFromDisk();
                    ShowToast(ToastKind.Info, "Reloaded after an external change.");
                    return;

                case ExternalChange.Modified:
                    var choice = await _dialogs.AskExternalChangeAsync(NotebookTitle, change);
                    if (choice == ExternalChangeChoice.ReloadFromDisk)
                    {
                        ReloadFromDisk();
                    }
                    else
                    {
                        // Keep the in-memory edits: re-baseline to the current on-disk content so the
                        // next save overwrites it, and so any *further* external change is still
                        // detected rather than silently suppressed.
                        _baselineHash = _notebooks.ComputeHash(_current.Path);
                    }

                    return;
            }
        }
        catch (Exception ex)
        {
            // A transient read failure during polling (the file briefly locked by a sync client,
            // antivirus, or another instance) must not crash the app; skip this tick and retry.
            _log.Debug(ex, "External-change check skipped for {Path}", _current?.Path);
        }
        finally
        {
            _externalCheckInProgress = false;
        }
    }

    private void ReloadFromDisk()
    {
        if (_current is null)
        {
            return;
        }

        try
        {
            AdoptLoaded(_notebooks.Load(_current.Path), SelectedNote?.Note.Id);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to reload notebook {Path}", _current.Path);
        }
    }

    // ----- View-model plumbing -------------------------------------------------------------------

    private void AdoptLoaded(LoadedNotebook loaded, string? selectNoteId)
    {
        _current = loaded;
        _baselineHash = loaded.ContentHash;
        _externalChangeAcknowledged = false;
        _dirty = false;
        _dirtyNoteIds.Clear();

        HasNotebook = true;
        NotebookTitle = string.IsNullOrWhiteSpace(loaded.Notebook.Title)
            ? Path.GetFileNameWithoutExtension(loaded.Path)
            : loaded.Notebook.Title;

        BuildNotes(loaded.Notebook, selectNoteId);
        SetSaveState(SaveState.Saved);
    }

    private void BuildNotes(Notebook notebook, string? selectNoteId)
    {
        _allNotes.Clear();
        foreach (var note in notebook.Notes)
        {
            _allNotes.Add(new NoteListItemViewModel(note, _config.DisplayTimeZone));
        }

        NotesFilter = string.Empty;
        RebuildNotes();
        SelectedNote = (selectNoteId is not null ? Notes.FirstOrDefault(n => n.Note.Id == selectNoteId) : null)
            ?? Notes.FirstOrDefault();
    }

    private void RebuildNotes()
    {
        var selected = SelectedNote;
        var selectedId = selected?.Note.Id;

        // Never hide the note currently being edited, even if it no longer matches the filter;
        // otherwise narrowing the filter would blank the editor and drop the selection.
        FilterInto(_allNotes, Notes, NotesFilter, n => n.Note.Title, n => ReferenceEquals(n, selected));

        if (selectedId is not null)
        {
            SelectedNote = Notes.FirstOrDefault(n => n.Note.Id == selectedId);
        }
    }

    private void RebuildRecents()
    {
        _allRecents.Clear();
        foreach (var path in _state.RecentNotebooks)
        {
            _allRecents.Add(new RecentNotebookItemViewModel(path) { IsMissing = !File.Exists(path) });
        }

        FilterInto(_allRecents, RecentNotebooks, RecentFilter, r => r.Name);
    }

    /// <summary>
    /// Repopulates <paramref name="target"/> from <paramref name="source"/>, keeping items whose
    /// <paramref name="textOf"/> contains <paramref name="filter"/> (case-insensitively), all items
    /// when the filter is blank, and any item matched by <paramref name="alwaysKeep"/>.
    /// </summary>
    private static void FilterInto<T>(
        IReadOnlyList<T> source,
        ObservableCollection<T> target,
        string filter,
        Func<T, string> textOf,
        Func<T, bool>? alwaysKeep = null)
    {
        target.Clear();
        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(filter)
                || (alwaysKeep?.Invoke(item) ?? false)
                || textOf(item).Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                target.Add(item);
            }
        }
    }

    private void AddRecent(string path)
    {
        _state.RecentNotebooks.RemoveAll(p => PathKey.Equal(p, path));
        _state.RecentNotebooks.Insert(0, Path.GetFullPath(path));
        if (_state.RecentNotebooks.Count > AppState.MaxRecentNotebooks)
        {
            _state.RecentNotebooks.RemoveRange(
                AppState.MaxRecentNotebooks,
                _state.RecentNotebooks.Count - AppState.MaxRecentNotebooks);
        }

        RebuildRecents();
    }

    private void LoadAttachments(Note? note)
    {
        DisposeAttachments();
        Attachments.Clear();
        if (note is null || _current is null)
        {
            return;
        }

        foreach (var attachment in NotebookStore.ResolveAttachments(_current.Path, note))
        {
            Attachments.Add(new AttachmentItemViewModel(attachment));
        }
    }

    private void DisposeAttachments()
    {
        foreach (var attachment in Attachments)
        {
            attachment.Dispose();
        }
    }

    /// <summary>Whether a captured note is still editable in the currently-open notebook (it survives reloads/closes).</summary>
    private bool IsLiveNote(Note note) =>
        _current is not null && !IsReadOnly && _current.Notebook.Notes.Contains(note);

    private void OnEditorEdited(object? sender, EventArgs e)
    {
        RefreshSelectedListItem();
        MarkDirty(Editor.Note?.Id);
    }

    private void MarkDirty(string? noteId)
    {
        if (!IsReady || _current is null || IsReadOnly)
        {
            return;
        }

        _dirty = true;
        if (noteId is not null)
        {
            _dirtyNoteIds.Add(noteId);
        }

        SetSaveState(SaveState.Unsaved);
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void RefreshSelectedListItem() => SelectedNote?.Refresh();

    private void ShowToast(ToastKind kind, string message) => _dialogs.Notify(kind, message);

    partial void OnNotesFilterChanged(string value) => RebuildNotes();

    partial void OnRecentFilterChanged(string value) => RebuildRecents();

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        Editor.Load(value?.Note);
        LoadAttachments(value?.Note);
        _state.CurrentNoteId = value?.Note.Id;
    }

    // ----- Configuration / state -----------------------------------------------------------------

    private void LoadConfigAndState()
    {
        try
        {
            _paths.EnsureCreated();
            _backups = new BackupStore(_paths.BackupDatabase);
            _config = _configStore.Load() ?? new AppConfig();
            _state = _stateStore.Load() ?? new AppState();
        }
        catch (Exception ex)
        {
            _loadError = ex;
            _config = new AppConfig();
            _state = new AppState();
        }
    }

    private void ApplyConfig()
    {
        EditorFontSize = _config.EditorFontSize;
        EditorFontFamily = new FontFamily(string.IsNullOrWhiteSpace(_config.EditorFont) ? "Inter" : _config.EditorFont);
        Editor.SetTimeZone(_config.DisplayTimeZone);
        _autosaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.25, _config.AutosaveDelaySeconds));
    }

    private void RestorePaneWidths()
    {
        RecentPaneWidth = _state.RecentPaneWidth;
        NotesPaneWidth = _state.NotesPaneWidth;
        AttachmentsPaneWidth = _state.AttachmentsPaneWidth;
    }

    private void PersistState()
    {
        if (!IsReady)
        {
            return;
        }

        _state.RecentPaneWidth = RecentPaneWidth;
        _state.NotesPaneWidth = NotesPaneWidth;
        _state.AttachmentsPaneWidth = AttachmentsPaneWidth;
        _state.CurrentNoteId = SelectedNote?.Note.Id;

        try
        {
            _stateStore.Save(_state);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save state");
        }
    }

    private void SetSaveState(SaveState state)
    {
        _saveState = state;
        UpdateSaveStateText();
    }

    private void UpdateSaveStateText() => SaveStateText = _saveState switch
    {
        SaveState.Saved => "Saved",
        SaveState.Saving => "Saving…",
        SaveState.Unsaved => "Unsaved changes",
        SaveState.Error => "Save failed",
        _ => string.Empty,
    };

    // ----- Helpers -------------------------------------------------------------------------------

    private static string EnsureDaynoteExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".daynote", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ".daynote";

    private static string UniqueFileName(string directory, string fileName)
    {
        var candidate = fileName;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{name} ({counter}){extension}";
            counter++;
        }

        return candidate;
    }
}
