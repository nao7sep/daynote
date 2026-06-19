using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DayNote.Core.Configuration;
using DayNote.Core.Identity;
using DayNote.Core.Models;
using DayNote.Core.Storage;
using DayNote.Desktop.Logging;
using DayNote.Desktop.Services;
using DayNote.Desktop.State;

namespace DayNote.Desktop.ViewModels;

/// <summary>
/// Orchestrates the main window: the four panes (recent notebooks, notes, editor, attachments),
/// load-gated configuration and state, opening and closing notebooks with per-notebook locking,
/// autosave with per-note dirty tracking, external-modification detection, and the recent-notebooks
/// list. Side-effecting file work is delegated to the Core storage layer; dialogs and the native
/// file picker go through <see cref="IDialogService"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppPaths _paths;
    private readonly NotebookStore _notebooks = new();
    private readonly JsonStore<AppConfig> _configStore;
    private readonly JsonStore<AppState> _stateStore;
    private readonly IDialogService _dialogs;
    private readonly IAppLogger _log;

    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _externalTimer;
    private readonly DispatcherTimer _savedFadeTimer;

    private readonly List<NoteListItemViewModel> _allNotes = new();
    private readonly List<RecentNotebookItemViewModel> _allRecents = new();
    private readonly HashSet<string> _dirtyNoteIds = new();

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

    public MainWindowViewModel(AppPaths paths, IDialogService dialogs, IAppLogger log)
    {
        _paths = paths;
        _dialogs = dialogs;
        _log = log;
        _configStore = new JsonStore<AppConfig>(paths.ConfigFile);
        _stateStore = new JsonStore<AppState>(paths.StateFile);

        // All startup I/O (directory creation, reading config/state) is gated here: any failure
        // becomes _loadError, which disables saving and surfaces an error dialog once the window is
        // shown, rather than crashing before any UI exists.
        LoadConfigAndState();

        Editor = new EditorViewModel(_config.DisplayTimeZone);
        Editor.Edited += OnEditorEdited;

        // The "Saved" status is transient: it shows briefly after a save, then clears itself (quickdeck
        // style). Non-idle states ("Saving…", "Unsaved changes", "Save failed") persist until they change.
        _savedFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _savedFadeTimer.Tick += (_, _) =>
        {
            _savedFadeTimer.Stop();
            if (_saveState == SaveState.Saved)
            {
                SaveStateText = string.Empty;
            }
        };

        _autosaveTimer = new DispatcherTimer();
        _autosaveTimer.Tick += async (_, _) =>
        {
            _autosaveTimer.Stop();
            await SaveCurrentAsync();
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

    /// <summary>Active toast notifications, rendered as a top-right overlay; each auto-dismisses.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    [ObservableProperty]
    private bool _isReady;

    [ObservableProperty]
    private string _saveStateText = "Saved";

    [ObservableProperty]
    private bool _hasNotebook;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _showAttachmentsEmptyHint;

    [ObservableProperty]
    private string _notebookTitle = "DayNote";

    [ObservableProperty]
    private string _notebookPath = string.Empty;

    [ObservableProperty]
    private string _notesFilter = string.Empty;

    [ObservableProperty]
    private string _recentFilter = string.Empty;

    [ObservableProperty]
    private NoteListItemViewModel? _selectedNote;

    // The highlighted notebook row. Selecting a row opens that notebook (single click or arrow key) via
    // OnSelectedRecentChanged — no double-click — so the highlight and the open notebook stay in sync.
    [ObservableProperty]
    private RecentNotebookItemViewModel? _selectedRecent;

    [ObservableProperty]
    private double _recentPaneWidth = 220;

    [ObservableProperty]
    private double _notesPaneWidth = 260;

    [ObservableProperty]
    private double _attachmentsPaneWidth = 260;

    [ObservableProperty]
    private FontFamily _editorFontFamily = new("Menlo");

    [ObservableProperty]
    private double _editorFontSize = 14;

    [ObservableProperty]
    private double _editorLineHeight = double.NaN;

    [ObservableProperty]
    private Thickness _editorPadding = new(12);

    [ObservableProperty]
    private FontWeight _editorFontWeight = FontWeight.Normal;

    [ObservableProperty]
    private FontStyle _editorFontStyle = FontStyle.Normal;

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

    /// <summary>Flushes pending work and releases the lock on shutdown. Pane widths are captured first by the view.</summary>
    public async Task ShutdownAsync()
    {
        _externalTimer.Stop();
        _autosaveTimer.Stop();
        _log.Info("Application shutting down", new { path = _current?.Path });

        // Persist state (including the current note id) while the notebook is still open;
        // CloseCurrentAsync clears the selection, which would otherwise null out CurrentNoteId.
        PersistState();
        await CloseCurrentAsync(clearSelection: false);
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
            _log.Warn("Recent notebook is missing", new { path = item.Path });
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
        // A brand-new note is the newest, so it goes to the top of the newest-first list.
        _allNotes.Insert(0, new NoteListItemViewModel(note, _config.DisplayTimeZone));
        NotesFilter = string.Empty;
        RebuildNotes();
        SelectedNote = Notes.FirstOrDefault(n => n.Note.Id == note.Id);
        MarkDirty(note.Id);
        _log.Info("Created note", new { noteId = note.Id });
    }

    [RelayCommand]
    private async Task DeleteNote(NoteListItemViewModel? item)
    {
        // The row's ✕ passes that row; the keyboard/menu path passes null and targets the selection.
        var target = item ?? SelectedNote;
        if (!IsReady || _current is null || IsReadOnly || target is null)
        {
            return;
        }

        var note = target.Note;
        var label = string.IsNullOrWhiteSpace(note.Title) ? "untitled" : note.Title;
        if (!await _dialogs.ConfirmAsync("Delete note", $"Delete “{label}”? Its attachment files are left on disk."))
        {
            return;
        }

        // The notebook may have been reloaded/closed while the confirm dialog was open; if the captured
        // note is no longer live, abort rather than mutate a stale notebook.
        if (!IsLiveNote(note))
        {
            return;
        }

        var deletingSelected = ReferenceEquals(target, SelectedNote);
        var index = Notes.IndexOf(target);

        _current.Notebook.Notes.Remove(note);
        _allNotes.RemoveAll(n => ReferenceEquals(n.Note, note));
        RebuildNotes();

        // Deleting the selected note recovers the selection to its neighbour (keeping the user's place,
        // not jumping to the top); deleting a different note via its ✕ leaves the selection untouched.
        if (deletingSelected)
        {
            SelectedNote = Notes.Count == 0 ? null : Notes[Math.Clamp(index, 0, Notes.Count - 1)];
        }

        MarkDirty(noteId: null);
        _log.Info("Deleted note", new { noteId = note.Id });
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

        // Hash the note's current attachments so a file whose content the note already has is not
        // copied again; a later identical file in the same batch dedups against earlier ones too.
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var existing in note.Attachments)
        {
            var existingPath = Path.Combine(directory, existing);
            if (!File.Exists(existingPath))
            {
                continue;
            }

            try
            {
                hashes.TryAdd(ContentHash.Sha256HexFile(existingPath), existing);
            }
            catch (Exception ex)
            {
                _log.Warn("Could not hash existing attachment for dedup", new { noteId = note.Id, file = existing }, ex);
            }
        }

        _log.Info("Adding attachments", new { noteId = note.Id, requested = files.Count });
        var added = 0;
        var duplicates = 0;
        var failed = 0;
        foreach (var source in files)
        {
            try
            {
                var hash = ContentHash.Sha256HexFile(source);
                if (hashes.ContainsKey(hash))
                {
                    duplicates++;
                    continue;
                }

                var name = UniqueFileName(directory, Path.GetFileName(source));
                File.Copy(source, Path.Combine(directory, name));
                note.Attachments.Add(name);
                hashes[hash] = name;
                added++;
            }
            catch (Exception ex)
            {
                failed++;
                _log.Error("Failed to add attachment", new { noteId = note.Id, source }, ex);
                ShowToast(ToastKind.Error, "Could not add " + Path.GetFileName(source));
            }
        }

        _log.Info("Attachments added", new { noteId = note.Id, added, duplicates, failed });
        if (duplicates > 0)
        {
            ShowToast(ToastKind.Info, duplicates == 1
                ? "Skipped a file already attached to this note."
                : $"Skipped {duplicates} files already attached to this note.");
        }

        if (added > 0)
        {
            LoadAttachments(note);
            MarkDirty(note.Id);
        }
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

        _log.Info("Removing attachment", new { noteId = note.Id, file = item.FileName });
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
            _log.Error("Failed to delete attachment", new { noteId = note.Id, path = item.FullPath }, ex);
        }

        LoadAttachments(note);
        MarkDirty(note.Id);
    }

    [RelayCommand]
    private async Task OpenAttachment(AttachmentItemViewModel item)
    {
        if (!File.Exists(item.FullPath))
        {
            return;
        }

        _log.Info("Opening attachment externally", new { file = item.FileName });
        try
        {
            await _dialogs.OpenPathExternallyAsync(item.FullPath);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open attachment externally", new { path = item.FullPath }, ex);
            ShowToast(ToastKind.Error, "Could not open " + item.FileName);
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
            _log.Info("Settings saved", ConfigSummary(_config));
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save settings", error: ex);
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
    private async Task OpenShortcuts()
    {
        _log.Info("Showing keyboard shortcuts");
        await _dialogs.ShowShortcutsAsync();
    }

    [RelayCommand]
    private async Task OpenAbout()
    {
        _log.Info("Showing about");
        await _dialogs.ShowAboutAsync();
    }

    [RelayCommand]
    private async Task SaveNow() => await SaveCurrentAsync();

    [RelayCommand]
    private void CycleTextStyle()
    {
        if (_config.TextStyles.Count == 0)
        {
            return;
        }

        var index = _config.TextStyles.FindIndex(s => string.Equals(s.Name, _config.SelectedTextStyle, StringComparison.OrdinalIgnoreCase));
        var next = _config.TextStyles[(index + 1) % _config.TextStyles.Count];
        _config.SelectedTextStyle = next.Name;
        ApplyTextStyle();
        _log.Info("Cycled text style", new { style = next.Name });
        if (IsReady)
        {
            try
            {
                _configStore.Save(_config);
            }
            catch (Exception ex)
            {
                _log.Error("Failed to save text-style preference", new { style = next.Name }, ex);
            }
        }

        ShowToast(ToastKind.Info, "Text style: " + next.Name);
    }

    // ----- Notebook open / close / save ----------------------------------------------------------

    private async Task OpenNotebookPathAsync(string path, bool isNew, string? selectNoteId = null)
    {
        if (!IsReady)
        {
            return;
        }

        // Re-selecting the notebook that is already open (e.g. tapping its row again) is a no-op.
        if (!isNew && _current is not null && PathKey.Equal(_current.Path, path))
        {
            return;
        }

        _log.Info("Opening notebook", new { path, isNew });
        var stopwatch = Stopwatch.StartNew();

        await CloseCurrentAsync(clearSelection: false);

        var acquired = NotebookLock.TryAcquire(_paths, path);
        var readOnly = false;
        if (acquired is null)
        {
            var choice = await _dialogs.AskLockedNotebookAsync(Path.GetFileNameWithoutExtension(path));
            if (choice == LockedNotebookChoice.Cancel)
            {
                _log.Info("Open cancelled: notebook is locked", new { path });
                return;
            }

            readOnly = true;
            _log.Warn("Notebook is locked by another instance; opening read-only", new { path });
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
            _log.Error("Failed to open notebook", new { path }, ex);
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

        _log.Info("Notebook opened", new
        {
            path = loaded.Path,
            isNew,
            readOnly,
            noteCount = loaded.Notebook.Notes.Count,
            durationMs = stopwatch.ElapsedMilliseconds,
        });

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

        _log.Info("Closing notebook", new { path = _current.Path });
        _autosaveTimer.Stop();
        if (!IsReadOnly && _dirty)
        {
            // Flush any pending edits before closing.
            await SaveCurrentAsync();
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

    private async Task SaveCurrentAsync()
    {
        if (!IsReady || _current is null || IsReadOnly || _externalCheckInProgress)
        {
            // Do not save while an external-change conflict is being resolved; the autosave Tick
            // reschedules so the edits are not lost.
            return;
        }

        if (!_dirty)
        {
            return;
        }

        _savingInProgress = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            SetSaveState(SaveState.Saving);
            var now = DateTimeOffset.UtcNow;
            var notebook = _current.Notebook;
            _log.Info("Saving notebook", new { path = _current.Path, noteCount = notebook.Notes.Count });

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

            Editor.RefreshMetadata();
            RefreshSelectedListItem();
            SetSaveState(SaveState.Saved);
            _log.Info("Notebook saved", new { path = saved.Path, chars = saved.Text.Length, durationMs = stopwatch.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save notebook", new { path = _current?.Path }, ex);
            SetSaveState(SaveState.Error);
            ShowToast(ToastKind.Error, "Save failed: " + ex.Message);
        }
        finally
        {
            _savingInProgress = false;
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
                    _log.Warn("Notebook file was deleted on disk", new { path = _current.Path });
                    ShowToast(ToastKind.Warning, "The notebook file was deleted. Your edits remain; saving will recreate it.");
                    return;

                case ExternalChange.Modified when !_dirty:
                    _log.Info("Notebook changed on disk; reloading", new { path = _current.Path });
                    ReloadFromDisk();
                    ShowToast(ToastKind.Info, "Reloaded after an external change.");
                    return;

                case ExternalChange.Modified:
                    var choice = await _dialogs.AskExternalChangeAsync(NotebookTitle, change);
                    if (choice == ExternalChangeChoice.ReloadFromDisk)
                    {
                        _log.Info("External change: reloading from disk, discarding local edits", new { path = _current.Path });
                        ReloadFromDisk();
                    }
                    else
                    {
                        // Keep the in-memory edits: re-baseline to the current on-disk content so the
                        // next save overwrites it, and so any *further* external change is still
                        // detected rather than silently suppressed.
                        _log.Info("External change: keeping local edits", new { path = _current.Path });
                        _baselineHash = _notebooks.ComputeHash(_current.Path);
                    }

                    return;
            }
        }
        catch (Exception ex)
        {
            // A transient read failure during polling (the file briefly locked by a sync client,
            // antivirus, or another instance) must not crash the app; skip this tick and retry.
            _log.Debug("External-change check skipped", new { path = _current?.Path }, ex);
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
            _log.Error("Failed to reload notebook", new { path = _current.Path }, ex);
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
        // Newest first (by creation time). Sorting by Created, not Modified, keeps the order stable
        // while editing; the stored file order is left untouched.
        foreach (var note in notebook.Notes.OrderByDescending(n => n.Created))
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

    /// <summary>Rebuilds the master recents list from state, then applies the current filter.</summary>
    private void RebuildRecents()
    {
        _allRecents.Clear();
        foreach (var path in _state.RecentNotebooks)
        {
            _allRecents.Add(new RecentNotebookItemViewModel(path) { IsMissing = !File.Exists(path) });
        }

        ApplyRecentFilter();
    }

    /// <summary>
    /// Re-applies the recents filter without rebuilding the master list, so typing in the filter does
    /// not churn the rows (and the open notebook's highlight survives keystroke to keystroke).
    /// </summary>
    private void ApplyRecentFilter()
    {
        // Flag the open notebook so its row shows the inline close affordance; keep it visible even when
        // the filter would exclude it, so filtering never hides the notebook you are working in.
        foreach (var recent in _allRecents)
        {
            recent.IsCurrent = _current is not null && PathKey.Equal(recent.Path, _current.Path);
        }

        FilterInto(_allRecents, RecentNotebooks, RecentFilter, r => r.Name, r => r.IsCurrent);

        // Keep the open notebook highlighted across rebuilds (open, filter, recents reorder).
        SelectedRecent = _current is null
            ? null
            : RecentNotebooks.FirstOrDefault(r => PathKey.Equal(r.Path, _current.Path));
    }

    /// <summary>
    /// Reconciles <paramref name="target"/> to the subset of <paramref name="source"/> that matches the
    /// filter (case-insensitively), keeping all items when the filter is blank and any item matched by
    /// <paramref name="alwaysKeep"/>. The reconcile is done in place — surviving rows (and the bound
    /// ListBox selection) are preserved rather than cleared and re-added, which would momentarily drop
    /// the selection and force the editor/attachments to reload on every keystroke.
    /// </summary>
    private static void FilterInto<T>(
        IReadOnlyList<T> source,
        ObservableCollection<T> target,
        string filter,
        Func<T, string> textOf,
        Func<T, bool>? alwaysKeep = null)
    {
        var desired = new List<T>(source.Count);
        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(filter)
                || (alwaysKeep?.Invoke(item) ?? false)
                || textOf(item).Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                desired.Add(item);
            }
        }

        // Drop rows no longer wanted, then bring the rest into the desired order, inserting newcomers.
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(target[i]))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], item))
            {
                continue;
            }

            var existing = target.IndexOf(item);
            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, item);
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
            ShowAttachmentsEmptyHint = false;
            return;
        }

        foreach (var attachment in NotebookStore.ResolveAttachments(_current.Path, note))
        {
            Attachments.Add(new AttachmentItemViewModel(attachment, _log));
        }

        ShowAttachmentsEmptyHint = Attachments.Count == 0;
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

    private const int MaxToasts = 4;
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(4);

    private void ShowToast(ToastKind kind, string message)
    {
        var toast = new ToastViewModel(kind, message);
        Toasts.Add(toast);
        while (Toasts.Count > MaxToasts)
        {
            Toasts.RemoveAt(0);
        }

        var timer = new DispatcherTimer { Interval = ToastLifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Toasts.Remove(toast);
        };
        timer.Start();
    }

    partial void OnNotesFilterChanged(string value) => RebuildNotes();

    partial void OnRecentFilterChanged(string value) => ApplyRecentFilter();

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        Editor.Load(value?.Note);
        LoadAttachments(value?.Note);
        _state.CurrentNoteId = value?.Note.Id;
    }

    partial void OnSelectedRecentChanged(RecentNotebookItemViewModel? value)
    {
        // Selecting a notebook opens it (single click or arrow key) — no double-click. Skip when
        // nothing is selected or it is already open; the command's own concurrency guard keeps rapid
        // selection changes from overlapping.
        if (value is null || (_current is not null && PathKey.Equal(_current.Path, value.Path)))
        {
            return;
        }

        if (OpenRecentCommand.CanExecute(value))
        {
            OpenRecentCommand.Execute(value);
        }
    }

    // ----- Configuration / state -----------------------------------------------------------------

    private void LoadConfigAndState()
    {
        try
        {
            _paths.EnsureCreated();
            _config = _configStore.Load() ?? new AppConfig();
            _state = _stateStore.Load() ?? new AppState();
            _log.Info("Configuration and state loaded", ConfigSummary(_config));
        }
        catch (Exception ex)
        {
            _loadError = ex;
            _config = new AppConfig();
            _state = new AppState();
            _log.Error("Failed to load configuration or state; saving disabled", new { root = _paths.Root }, ex);
        }
    }

    private void ApplyConfig()
    {
        ApplyTextStyle();
        Editor.SetTimeZone(_config.DisplayTimeZone);
        _autosaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.25, _config.AutosaveDelaySeconds));
    }

    /// <summary>Applies the selected text-style preset (or the first available) to the editor properties.</summary>
    private void ApplyTextStyle()
    {
        var style = _config.TextStyles.FirstOrDefault(s => string.Equals(s.Name, _config.SelectedTextStyle, StringComparison.OrdinalIgnoreCase))
            ?? _config.TextStyles.FirstOrDefault();
        if (style is null)
        {
            return;
        }

        EditorFontFamily = new FontFamily(string.IsNullOrWhiteSpace(style.FontFamily) ? "Menlo" : style.FontFamily);
        EditorFontSize = style.FontSize;
        // LineHeight is absolute; NaN lets the control use the font's natural leading.
        EditorLineHeight = style.LineSpacing > 0 ? style.FontSize * style.LineSpacing : double.NaN;
        EditorPadding = new Thickness(style.Padding);
        EditorFontWeight = style.Bold ? FontWeight.Bold : FontWeight.Normal;
        EditorFontStyle = style.Italic ? FontStyle.Italic : FontStyle.Normal;
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
            _log.Error("Failed to save state", error: ex);
        }
    }

    private void SetSaveState(SaveState state)
    {
        _saveState = state;
        UpdateSaveStateText();
    }

    private void UpdateSaveStateText()
    {
        _savedFadeTimer.Stop();
        SaveStateText = _saveState switch
        {
            SaveState.Saved => "Saved",
            SaveState.Saving => "Saving…",
            SaveState.Unsaved => "Unsaved changes",
            SaveState.Error => "Save failed",
            _ => string.Empty,
        };

        // Only the idle "Saved" message fades; the others stay until the state changes.
        if (_saveState == SaveState.Saved)
        {
            _savedFadeTimer.Start();
        }
    }

    // ----- Helpers -------------------------------------------------------------------------------

    /// <summary>The key effective configuration, summarized for the log (no secret-bearing fields).</summary>
    private static object ConfigSummary(AppConfig config) => new
    {
        selectedTextStyle = config.SelectedTextStyle,
        textStyleCount = config.TextStyles.Count,
        autosaveDelaySeconds = config.AutosaveDelaySeconds,
        displayTimeZone = config.DisplayTimeZone,
    };

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
