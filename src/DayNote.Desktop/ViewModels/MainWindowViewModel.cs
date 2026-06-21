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
using DayNote.Core.Text;
using DayNote.Desktop.Logging;
using DayNote.Desktop.Services;
using DayNote.Desktop.State;

namespace DayNote.Desktop.ViewModels;

/// <summary>
/// Orchestrates the main window: the four panes (binders, notes, editor, attachments),
/// load-gated configuration and state, opening/closing binders, autosave with per-note dirty
/// tracking, external-modification detection, and the known-binders list. Multiple instances may
/// open the same binder concurrently — there is no lock; external-change detection reconciles
/// concurrent edits. Side-effecting file work is delegated to the Core storage layer; dialogs and
/// the native file picker go through <see cref="IDialogService"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppPaths _paths;
    private readonly BinderStore _binderStore = new();
    private readonly JsonStore<AppConfig> _configStore;
    private readonly JsonStore<AppState> _stateStore;
    private readonly IDialogService _dialogs;
    private readonly IAppLogger _log;

    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _externalTimer;
    private readonly DispatcherTimer _savedFadeTimer;

    private readonly List<NoteListItemViewModel> _allNotes = new();
    private readonly List<BinderListItemViewModel> _allBinders = new();
    private readonly HashSet<string> _dirtyNoteIds = new();

    private AppConfig _config = new();
    private AppState _state = new();
    private Exception? _loadError;

    private LoadedBinder? _current;
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
            RebuildBinders();
            IsReady = true;
        }

        UpdateSaveStateText();
    }

    public EditorViewModel Editor { get; }

    public ObservableCollection<NoteListItemViewModel> Notes { get; } = new();
    public ObservableCollection<BinderListItemViewModel> Binders { get; } = new();
    public ObservableCollection<AttachmentItemViewModel> Attachments { get; } = new();

    /// <summary>Active toast notifications, rendered as a top-right overlay; each auto-dismisses.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    [ObservableProperty]
    private bool _isReady;

    [ObservableProperty]
    private string _saveStateText = "Saved";

    [ObservableProperty]
    private bool _hasBinder;

    [ObservableProperty]
    private bool _showAttachmentsEmptyHint;

    /// <summary>True while files are being dragged over the attachments pane (drives the drop highlight).</summary>
    [ObservableProperty]
    private bool _isAttachmentDropActive;

    [ObservableProperty]
    private string _binderTitle = "DayNote";

    [ObservableProperty]
    private string _notesFilter = string.Empty;

    [ObservableProperty]
    private string _bindersFilter = string.Empty;

    [ObservableProperty]
    private NoteListItemViewModel? _selectedNote;

    // The highlighted binder row. Selecting a row opens that binder (single click or arrow key) via
    // OnSelectedBinderChanged — no double-click — so the highlight and the open binder stay in sync.
    [ObservableProperty]
    private BinderListItemViewModel? _selectedBinder;

    [ObservableProperty]
    private double _bindersPaneWidth = 220;

    [ObservableProperty]
    private double _notesPaneWidth = 260;

    [ObservableProperty]
    private double _editorPaneWidth = 430;

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

        if (!string.IsNullOrEmpty(_state.CurrentBinderPath) && File.Exists(_state.CurrentBinderPath))
        {
            await OpenBinderPathAsync(_state.CurrentBinderPath!, isNew: false, selectNoteId: _state.CurrentNoteId);
        }

        _externalTimer.Start();
    }

    /// <summary>Flushes pending work on shutdown. Pane widths are captured first by the view.</summary>
    public async Task ShutdownAsync()
    {
        _externalTimer.Stop();
        _autosaveTimer.Stop();
        _log.Info("Application shutting down", new { path = _current?.Path });

        // Persist state (including the current note id) while the binder is still open;
        // CloseCurrentAsync clears the selection, which would otherwise null out CurrentNoteId.
        PersistState();
        await CloseCurrentAsync(clearSelection: false);
    }

    // ----- Commands ------------------------------------------------------------------------------

    [RelayCommand]
    private async Task NewBinder()
    {
        if (!IsReady)
        {
            return;
        }

        var path = await _dialogs.PickBinderToCreateAsync();
        if (path is null)
        {
            return;
        }

        await OpenBinderPathAsync(EnsureDaynoteExtension(path), isNew: true);
    }

    [RelayCommand]
    private async Task OpenBinder()
    {
        if (!IsReady)
        {
            return;
        }

        var path = await _dialogs.PickBinderToOpenAsync();
        if (path is not null)
        {
            await OpenBinderPathAsync(path, isNew: false);
        }
    }

    [RelayCommand]
    private async Task OpenKnownBinder(BinderListItemViewModel item)
    {
        if (!File.Exists(item.Path))
        {
            item.IsMissing = true;
            _log.Warn("Known binder is missing from disk", new { path = item.Path });
            ShowToast(ToastKind.Warning, "That binder is no longer at " + item.Path);
            return;
        }

        await OpenBinderPathAsync(item.Path, isNew: false);
    }

    /// <summary>
    /// Closes the open binder and forgets it (removes it from the list) — closing <em>is</em> forgetting,
    /// matching the "known binders" model. Bound to Cmd/Ctrl+W.
    /// </summary>
    [RelayCommand]
    private async Task CloseBinder()
    {
        if (_current is null)
        {
            return;
        }

        var path = _current.Path;
        await CloseCurrentAsync(clearSelection: true);
        ForgetBinder(path);
    }

    /// <summary>
    /// The row's ✕: removes a binder from the list. If it is the open one, it is closed (and its edits
    /// flushed) first; otherwise the open binder is untouched. This also clears a stale/missing entry.
    /// </summary>
    [RelayCommand]
    private async Task RemoveBinder(BinderListItemViewModel item)
    {
        if (!IsReady)
        {
            return;
        }

        if (_current is not null && PathKey.Equal(_current.Path, item.Path))
        {
            await CloseCurrentAsync(clearSelection: true);
        }

        ForgetBinder(item.Path);
    }

    [RelayCommand]
    private void NewNote()
    {
        if (!IsReady || _current is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var note = new Note
        {
            Id = IdGenerator.NewUnique(_current.Binder.Notes.Select(n => n.Id)),
            Title = string.Empty,
            Created = now,
            Modified = now,
            Body = string.Empty,
        };

        _current.Binder.Notes.Add(note);
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
        if (!IsReady || _current is null || target is null)
        {
            return;
        }

        var note = target.Note;
        var label = string.IsNullOrWhiteSpace(note.Title) ? "untitled" : note.Title;
        if (!await _dialogs.ConfirmAsync("Delete note", $"Delete “{label}”? Its attachment files are left on disk."))
        {
            return;
        }

        // The binder may have been reloaded/closed while the confirm dialog was open; if the captured
        // note is no longer live, abort rather than mutate a stale binder.
        if (!IsLiveNote(note))
        {
            return;
        }

        var deletingSelected = ReferenceEquals(target, SelectedNote);
        var index = Notes.IndexOf(target);

        _current.Binder.Notes.Remove(note);
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
        if (!IsReady || _current is null || SelectedNote is null)
        {
            return;
        }

        var files = await _dialogs.PickAttachmentsAsync();
        AddAttachmentFiles(files);
    }

    /// <summary>Adds files dropped onto the attachments pane (same path as the Add button).</summary>
    public void AddDroppedFiles(IReadOnlyList<string> files) => AddAttachmentFiles(files);

    /// <summary>
    /// Live reorder step during a drag: moves <paramref name="item"/> to <paramref name="newIndex"/> in
    /// the visible list only (no persistence). The order is committed once on release via
    /// <see cref="CommitAttachmentOrder"/>.
    /// </summary>
    public void MoveAttachment(AttachmentItemViewModel item, int newIndex)
    {
        var oldIndex = Attachments.IndexOf(item);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Attachments.Count || newIndex == oldIndex)
        {
            return;
        }

        Attachments.Move(oldIndex, newIndex);
    }

    /// <summary>Persists the current attachment order to the note (called when a reorder drag ends).</summary>
    public void CommitAttachmentOrder()
    {
        if (!IsReady || _current is null || SelectedNote is null)
        {
            return;
        }

        var note = SelectedNote.Note;
        if (!IsLiveNote(note) || note.Attachments.SequenceEqual(Attachments.Select(a => a.FileName)))
        {
            return;
        }

        note.Attachments.Clear();
        foreach (var attachment in Attachments)
        {
            note.Attachments.Add(attachment.FileName);
        }

        MarkDirty(note.Id);
        _log.Info("Reordered attachments", new { noteId = note.Id });
    }

    private void AddAttachmentFiles(IReadOnlyList<string> files)
    {
        if (!IsReady || _current is null || SelectedNote is null)
        {
            return;
        }

        var note = SelectedNote.Note;
        if (files.Count == 0 || !IsLiveNote(note))
        {
            return;
        }

        var directory = BinderStore.NoteAssetsDirectory(_current.Path, note.Id);
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
        if (!IsReady || _current is null || SelectedNote is null)
        {
            return;
        }

        var note = SelectedNote.Note;
        if (!await _dialogs.ConfirmAsync("Remove attachment", $"Remove “{item.FileName}”? The file will be deleted."))
        {
            return;
        }

        // The binder may have been reloaded (replacing note objects) while the dialog was open;
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

    // ----- Binder open / close / save ----------------------------------------------------------

    private async Task OpenBinderPathAsync(string path, bool isNew, string? selectNoteId = null)
    {
        if (!IsReady)
        {
            return;
        }

        // Re-selecting the binder that is already open (e.g. tapping its row again) is a no-op.
        if (!isNew && _current is not null && PathKey.Equal(_current.Path, path))
        {
            return;
        }

        _log.Info("Opening binder", new { path, isNew });
        var stopwatch = Stopwatch.StartNew();

        await CloseCurrentAsync(clearSelection: false);

        LoadedBinder loaded;
        try
        {
            if (isNew)
            {
                var binder = new Binder
                {
                    Id = IdGenerator.New(),
                    Created = DateTimeOffset.UtcNow,
                    Modified = DateTimeOffset.UtcNow,
                };
                var saved = _binderStore.Save(path, binder);
                loaded = new LoadedBinder(binder, saved.Path, saved.ContentHash);
            }
            else
            {
                loaded = _binderStore.Load(path);
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open binder", new { path }, ex);
            await _dialogs.ShowErrorAsync("Could not open binder", ex.Message);
            return;
        }

        AdoptLoaded(loaded, selectNoteId);

        AddBinder(loaded.Path);
        _state.CurrentBinderPath = loaded.Path;
        PersistState();

        _log.Info("Binder opened", new
        {
            path = loaded.Path,
            isNew,
            noteCount = loaded.Binder.Notes.Count,
            durationMs = stopwatch.ElapsedMilliseconds,
        });
    }

    private async Task CloseCurrentAsync(bool clearSelection)
    {
        if (_current is null)
        {
            return;
        }

        _log.Info("Closing binder", new { path = _current.Path });
        _autosaveTimer.Stop();
        if (_dirty)
        {
            // Flush any pending edits before closing.
            await SaveCurrentAsync();
        }

        _current = null;
        HasBinder = false;
        _dirty = false;
        _dirtyNoteIds.Clear();
        Editor.Load(null);
        _allNotes.Clear();
        Notes.Clear();
        DisposeAttachments();
        Attachments.Clear();
        SelectedNote = null;
        BinderTitle = "DayNote";
        SetSaveState(SaveState.Saved);

        if (clearSelection)
        {
            _state.CurrentBinderPath = null;
            _state.CurrentNoteId = null;
            PersistState();
        }
    }

    private async Task SaveCurrentAsync()
    {
        if (!IsReady || _current is null || _externalCheckInProgress)
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
            var binder = _current.Binder;
            _log.Info("Saving binder", new { path = _current.Path, noteCount = binder.Notes.Count });

            foreach (var id in _dirtyNoteIds)
            {
                var note = binder.Notes.FirstOrDefault(n => n.Id == id);
                if (note is not null)
                {
                    note.Modified = now;
                }
            }

            if (_dirty)
            {
                binder.Modified = now;
            }

            var saved = _binderStore.Save(_current.Path, binder);
            _baselineHash = saved.ContentHash;
            _externalChangeAcknowledged = false;
            _dirty = false;
            _dirtyNoteIds.Clear();

            Editor.RefreshMetadata();
            RefreshSelectedListItem();
            SetSaveState(SaveState.Saved);
            _log.Info("Binder saved", new { path = saved.Path, chars = saved.Text.Length, durationMs = stopwatch.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save binder", new { path = _current?.Path }, ex);
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
        if (!IsReady || _current is null || _externalChangeAcknowledged
            || _externalCheckInProgress || _savingInProgress)
        {
            return;
        }

        _externalCheckInProgress = true;
        try
        {
            var change = _binderStore.CheckExternalChange(_current.Path, _baselineHash);
            switch (change)
            {
                case ExternalChange.None:
                    return;

                case ExternalChange.Deleted:
                    _externalChangeAcknowledged = true;
                    _log.Warn("Binder file was deleted on disk", new { path = _current.Path });
                    ShowToast(ToastKind.Warning, "The binder file was deleted. Your edits remain; saving will recreate it.");
                    return;

                case ExternalChange.Modified when !_dirty:
                    _log.Info("Binder changed on disk; reloading", new { path = _current.Path });
                    ReloadFromDisk();
                    ShowToast(ToastKind.Info, "Reloaded after an external change.");
                    return;

                case ExternalChange.Modified:
                    var choice = await _dialogs.AskExternalChangeAsync(BinderTitle, change);
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
                        _baselineHash = _binderStore.ComputeHash(_current.Path);
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
            AdoptLoaded(_binderStore.Load(_current.Path), SelectedNote?.Note.Id);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to reload binder", new { path = _current.Path }, ex);
        }
    }

    // ----- View-model plumbing -------------------------------------------------------------------

    private void AdoptLoaded(LoadedBinder loaded, string? selectNoteId)
    {
        _current = loaded;
        _baselineHash = loaded.ContentHash;
        _externalChangeAcknowledged = false;
        _dirty = false;
        _dirtyNoteIds.Clear();

        HasBinder = true;
        BinderTitle = TitleFor(loaded.Path);

        BuildNotes(loaded.Binder, selectNoteId);
        SetSaveState(SaveState.Saved);
    }

    private void BuildNotes(Binder binder, string? selectNoteId)
    {
        _allNotes.Clear();
        // Newest first (by creation time). Sorting by Created, not Modified, keeps the order stable
        // while editing; the stored file order is left untouched.
        foreach (var note in binder.Notes.OrderByDescending(n => n.Created))
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

    /// <summary>Rebuilds the master binders list from state, then applies the current filter.</summary>
    private void RebuildBinders()
    {
        _allBinders.Clear();
        foreach (var entry in _state.Binders)
        {
            _allBinders.Add(new BinderListItemViewModel(entry.Path)
            {
                IsMissing = !File.Exists(entry.Path),
                Title = TitleFor(entry.Path),
            });
        }

        ApplyBinderFilter();
    }

    /// <summary>
    /// The binder's display title: its locally-stored title from app state, or the file name when no
    /// title is stored. Titles live in state (not the .daynote file), so this needs no file I/O.
    /// </summary>
    private string TitleFor(string path)
    {
        var title = _state.Binders.FirstOrDefault(b => PathKey.Equal(b.Path, path))?.Title;
        return string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title;
    }

    /// <summary>
    /// Applies an inline title edit to a binder. The title is a local label stored in app state (never
    /// in the .daynote file), so this just updates that state entry and persists. A blank or unchanged
    /// title is ignored. Called by the view on blur / Enter.
    /// </summary>
    public void ApplyBinderRename(BinderListItemViewModel item, string rawTitle)
    {
        if (!item.IsEditing)
        {
            return;
        }

        item.IsEditing = false;
        var newTitle = TextCleanup.SingleLine(rawTitle ?? string.Empty);
        if (string.IsNullOrEmpty(newTitle) || string.Equals(newTitle, item.Title, StringComparison.Ordinal))
        {
            return;
        }

        var entry = _state.Binders.FirstOrDefault(b => PathKey.Equal(b.Path, item.Path));
        if (entry is null)
        {
            return; // not a known binder (shouldn't happen for a visible row)
        }

        entry.Title = newTitle;
        item.Title = newTitle;
        if (_current is not null && PathKey.Equal(_current.Path, item.Path))
        {
            BinderTitle = newTitle;
        }

        PersistState();
        _log.Info("Renamed binder", new { path = item.Path });
    }

    /// <summary>
    /// Re-applies the binder filter without rebuilding the master list, so typing in the filter does
    /// not churn the rows (and the open binder's highlight survives keystroke to keystroke).
    /// </summary>
    private void ApplyBinderFilter()
    {
        // Flag the open binder so its row shows the inline close affordance; keep it visible even when
        // the filter would exclude it, so filtering never hides the binder you are working in.
        foreach (var binder in _allBinders)
        {
            binder.IsCurrent = _current is not null && PathKey.Equal(binder.Path, _current.Path);
        }

        // Match the displayed title and the file name, so the filter lines up with what the row shows.
        FilterInto(_allBinders, Binders, BindersFilter, r => r.Title + " " + r.Name, r => r.IsCurrent);

        // Keep the open binder highlighted across rebuilds (open, filter, prune).
        SelectedBinder = _current is null
            ? null
            : Binders.FirstOrDefault(r => PathKey.Equal(r.Path, _current.Path));
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

    // Records a binder as known. An already-known binder keeps its place (the list is a stable managed
    // set, not a reshuffling MRU); a new one is added at the top, titled with its file name initially.
    // No cap — the user prunes explicitly via the row ✕, so a known binder never silently disappears.
    private void AddBinder(string path)
    {
        var full = Path.GetFullPath(path);
        if (_state.Binders.Any(b => PathKey.Equal(b.Path, full)))
        {
            // Already known: don't churn the list (which is this ListBox's ItemsSource); just refresh
            // which row is marked current.
            ApplyBinderFilter();
            return;
        }

        _state.Binders.Insert(0, new KnownBinder { Path = full, Title = Path.GetFileNameWithoutExtension(full) });
        RebuildBinders();
    }

    // Removes a binder from the known list and persists. Caller closes it first if it is the open one.
    private void ForgetBinder(string path)
    {
        _state.Binders.RemoveAll(b => PathKey.Equal(b.Path, path));
        PersistState();
        RebuildBinders();
        _log.Info("Forgot binder", new { path });
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

        foreach (var attachment in BinderStore.ResolveAttachments(_current.Path, note))
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

    /// <summary>Whether a captured note is still editable in the currently-open binder (it survives reloads/closes).</summary>
    private bool IsLiveNote(Note note) =>
        _current is not null && _current.Binder.Notes.Contains(note);

    private void OnEditorEdited(object? sender, EventArgs e)
    {
        RefreshSelectedListItem();
        MarkDirty(Editor.Note?.Id);
    }

    private void MarkDirty(string? noteId)
    {
        if (!IsReady || _current is null)
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

    partial void OnBindersFilterChanged(string value) => ApplyBinderFilter();

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        Editor.Load(value?.Note);
        LoadAttachments(value?.Note);
        _state.CurrentNoteId = value?.Note.Id;
    }

    partial void OnSelectedBinderChanged(BinderListItemViewModel? value)
    {
        // Selecting a binder opens it (single click or arrow key) — no double-click. Skip when
        // nothing is selected or it is already open.
        if (value is null || (_current is not null && PathKey.Equal(_current.Path, value.Path)))
        {
            return;
        }

        // Defer the open: it rebuilds the binders list — this very ListBox's ItemsSource — and mutating
        // it synchronously inside the selection-changed notification corrupts Avalonia's selection model
        // (an ArgumentOutOfRangeException that crashes the app). Posting runs the open after the
        // selection commit has finished.
        var target = value;
        Dispatcher.UIThread.Post(() =>
        {
            if (_current is not null && PathKey.Equal(_current.Path, target.Path))
            {
                return;
            }

            if (OpenKnownBinderCommand.CanExecute(target))
            {
                OpenKnownBinderCommand.Execute(target);
            }
        });
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
        BindersPaneWidth = _state.BindersPaneWidth;
        NotesPaneWidth = _state.NotesPaneWidth;
        EditorPaneWidth = _state.EditorPaneWidth;
        AttachmentsPaneWidth = _state.AttachmentsPaneWidth;
    }

    private void PersistState()
    {
        if (!IsReady)
        {
            return;
        }

        _state.BindersPaneWidth = BindersPaneWidth;
        _state.NotesPaneWidth = NotesPaneWidth;
        _state.EditorPaneWidth = EditorPaneWidth;
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
