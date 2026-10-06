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
using DayNote.Core.Time;
using DayNote.I18n;
using DayNote.Logging;
using DayNote.Services;
using DayNote.State;

namespace DayNote.ViewModels;

/// <summary>
/// Orchestrates the main window: the four panes (binders, notes, editor, attachments),
/// load-gated configuration and state, opening/closing binders, autosave with per-note dirty
/// tracking, external-modification detection, and the known-binders list. One GUI process owns a
/// storage root at a time; external-change detection still reconciles edits from other tools and
/// sync clients. Side-effecting file work is delegated to the Core storage layer; dialogs and the
/// native file picker go through <see cref="IDialogService"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IRecordsWindowHost
{
    // The subjects of app-shell results. Each holds at most one result, so these six are also the
    // most results the shell can show at once.
    private const string SaveFailureResultKey = "binder-save-failure";
    private const string NewBinderPickerResultKey = "new-binder-picker";
    private const string OpenBinderPickerResultKey = "open-binder-picker";
    private const string BinderFileResultKey = "binder-file";
    private const string AttachmentCleanupResultKey = "attachment-cleanup";
    private const string ConfigSaveFailureResultKey = "config-save-failure";

    private const string AttachmentPickerResultKey = "attachment-picker";

    private readonly AppPaths _paths;
    private readonly BinderStore _binderStore;
    private readonly ConfigStore _configStore;
    private readonly JsonStore<AppState> _stateStore;
    private readonly IDialogService _dialogs;
    private readonly IAppLogger _log;
    private readonly Action<string> _deleteFile;
    private readonly Action<string> _deleteDirectory;

    // The clock every recorded time is read from: created, modified and status times.
    private readonly TimeProvider _clock;

    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _textStyleStatusTimer;
    private readonly DispatcherTimer _externalTimer;

    private readonly List<NoteListItemViewModel> _allNotes = new();
    private readonly List<BinderListItemViewModel> _allBinders = new();
    private readonly HashSet<string> _dirtyNoteIds = new();

    // Each note's content and Modified time as they were last loaded or saved. A change that leaves the
    // content as saved keeps the saved Modified; one that changes it sets Modified to the moment of the
    // edit, so status and lock changes, and edits undone before the save, leave Modified alone
    // (content-lifecycle-conventions).
    private readonly Dictionary<string, SavedNote> _saved = new();

    // Each note's content as of its latest change, so a change that leaves the content as it was (a
    // status or lock change) is told apart from an edit.
    private readonly Dictionary<string, NoteContent> _lastContent = new();

    // The binder's notes and its Modified time as last loaded or saved, and when a note was last added or
    // deleted. The binder's content is its notes', so its Modified is the latest edit among them, and it
    // keeps the saved time when its content is back to what was saved (content-lifecycle-conventions).
    private string[] _savedNoteIds = [];
    private DateTimeOffset _savedBinderModified;
    private DateTimeOffset _notesChangedAt;
    private string? _attachmentNoteId;

    private AppConfig _config = new();

    // The zone times are shown in, resolved from the configuration whenever it is applied. Rows and the
    // editor are handed it on every refresh rather than keeping their own copy.
    private TimeZoneInfo _displayZone = TimeZoneInfo.Local;
    private AppState _state = new();

    // Why the settings file could not be used at launch: it could not be read or set aside, or a newer
    // DayNote wrote it. While set, nothing is saved.
    private Exception? _configLoadError;

    private LoadedBinder? _current;
    private string _baselineHash = string.Empty;
    private bool _dirty;
    private bool _externalChangeAcknowledged;
    private SaveState _saveState = SaveState.Saved;

    // The one owner of the open binder's file and its baseline hash. A save holds it from its snapshot
    // until its result is applied, and the external-change check holds it from its read until its
    // reload or re-baseline is applied, so neither ever acts on the file or the baseline while the
    // other is between its I/O and its result: two saves never overlap on the file, a check never
    // mistakes the app's own in-flight write for an external edit, and a save never lands on top of
    // content the check just reloaded. A save that arrives during a check, conflict question included,
    // waits its turn, so a quit or a switch is never refused for it. The UI thread stays the only place
    // that takes or releases it.
    private readonly SemaphoreSlim _binderFileLock = new(1, 1);

    // Bumped by every MarkDirty call. A save snapshots this right before it hands its text to the
    // background writer; if it changes before that write returns, an edit landed mid-save, and the
    // save must not clear _dirty/_dirtyNoteIds on completion — doing so unconditionally would silently
    // discard that newer edit (it would never be written, and never be flagged as unsaved again).
    private long _dirtyGeneration;

    // Bumped by every OpenBinderPathAsync call right before its background read/write starts. If it
    // changes before that call returns, a later open has already started — the user moved on to
    // another binder while this one's file was still being read — and this call's result is discarded
    // instead of adopting a binder the user is no longer looking at.
    private long _openGeneration;

    public MainWindowViewModel(
        AppPaths paths,
        IDialogService dialogs,
        IAppLogger log,
        Action<string>? deleteFile = null,
        Action<string>? deleteDirectory = null,
        TimeProvider? clock = null,
        BinderStore? binderStore = null)
    {
        _paths = paths;
        _dialogs = dialogs;
        _log = log;
        _deleteFile = deleteFile ?? File.Delete;
        _deleteDirectory = deleteDirectory ?? (path => Directory.Delete(path, recursive: true));
        _clock = clock ?? TimeProvider.System;
        _binderStore = binderStore ?? new BinderStore();
        _configStore = new ConfigStore(paths.ConfigFile, key => _log.Warn("Invalid configuration set; using built-in", new { key }));
        _stateStore = new JsonStore<AppState>(paths.StateFile, FormatVersions.State, recordBackup: false);

        // All startup I/O (directory creation, reading config/state) is gated here, rather than
        // crashing before any UI exists: a settings file that cannot be read halts with saving
        // disabled and an error dialog once the window is shown; a state file that cannot be read
        // only costs the remembered view.
        LoadConfigAndState();
        _displayZone = DayNoteTime.DisplayZone(_config.TimeZone);

        Editor = new EditorViewModel(_displayZone, _clock);
        Editor.Changed += OnEditorChanged;
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EditorViewModel.IsEditable))
            {
                OnPropertyChanged(nameof(CanEditNote));
            }
        };

        // Empty-state text is derived from the live collections. Collection notifications keep the
        // mandatory pane overlays in sync without maintaining parallel counts or visibility flags.
        Binders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(BindersEmptyStateText));
        Notes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NotesEmptyStateText));
        Attachments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AttachmentsEmptyStateText));
        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasResults));
            // A result that leaves stops being the announcement, so the same message shown again
            // later is a new announcement rather than an unchanged name.
            if (AnnouncedResult is { } announced && !Results.Contains(announced))
            {
                AnnouncedResult = null;
            }
        };

        _textStyleStatusTimer = new DispatcherTimer { Interval = TextStyleStatusLifetime };
        _textStyleStatusTimer.Tick += (_, _) =>
        {
            _textStyleStatusTimer.Stop();
            _textStyleStatus = null;
            OnPropertyChanged(nameof(TextStyleStatusText));
        };

        _autosaveTimer = new DispatcherTimer();
        _autosaveTimer.Tick += async (_, _) =>
        {
            _autosaveTimer.Stop();
            await SaveCurrentAsync();
            if (_dirty)
            {
                // The save failed, or an edit landed while it was writing; reschedule so the edits
                // are retried rather than stranded.
                _autosaveTimer.Start();
            }
        };

        _externalTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _externalTimer.Tick += async (_, _) =>
        {
            RefreshKnownBinders();
            await CheckExternalChangeAsync();
        };

        if (_configLoadError is null)
        {
            ApplyConfig();
            RestorePaneWidths();
            RebuildBinders();
            IsReady = true;
        }

        UpdateSaveStateText();
    }

    public EditorViewModel Editor { get; }

    /// <summary>
    /// The computer's own languages, in order, as <c>LanguageBootstrap</c> read them at launch. A
    /// language saved in Settings resolves System against these, never against a fresh reading, so
    /// System cannot mean two languages in one session.
    /// </summary>
    internal IReadOnlyList<string> ComputerLanguages { get; init; } = [];

    /// <summary>
    /// Brings every word this view model has on screen into the current language. The window calls it
    /// when the language changes while it is open: an empty name tells every binding on this view model
    /// to re-read, and the rows, the editor and the results, which hold their own words, re-announce
    /// theirs.
    /// </summary>
    internal void Retranslate()
    {
        OnPropertyChanged(string.Empty);
        Editor.Retranslate();
        foreach (var note in _allNotes)
        {
            note.Refresh(_displayZone);
        }

        foreach (var attachment in Attachments)
        {
            attachment.Retranslate();
        }

        foreach (var result in Results)
        {
            result.Retranslate();
        }

        AttachmentResult?.Retranslate();
    }

    public ObservableCollection<NoteListItemViewModel> Notes { get; } = new();
    public ObservableCollection<BinderListItemViewModel> Binders { get; } = new();
    public ObservableCollection<AttachmentItemViewModel> Attachments { get; } = new();

    /// <summary>
    /// Whether the selected note's content may be changed. A locked note's content is locked, and its
    /// attachments are part of the note rather than a list beside it, so adding, removing, and
    /// reordering them answer to the same rule as its title and body. Its status and deletion stay free.
    /// </summary>
    public bool CanEditNote => SelectedNote is not null && Editor.IsEditable;

    /// <summary>Active app-shell results, newest first, rendered as a stack over the pane track.</summary>
    public ObservableCollection<OperationResultViewModel> Results { get; } = new();

    public bool HasResults => Results.Count > 0;

    /// <summary>
    /// The result most recently shown, while it remains. The view announces it through one stable
    /// live region: platforms announce a live region when its name changes, not when a new element
    /// appears, so a newly added result card would otherwise go unannounced.
    /// </summary>
    [ObservableProperty]
    private OperationResultViewModel? _announcedResult;

    /// <summary>
    /// The text style just applied, which the status bar shows briefly and then drops, per the
    /// app-chrome-conventions.
    /// </summary>
    public string TextStyleStatusText => _textStyleStatus is null ? string.Empty : Localizer.Of(_textStyleStatus);

    private Message? _textStyleStatus;

    /// <summary>
    /// The saved theme. The view applies it app-wide (AppTheme) at startup and whenever Settings
    /// commits a change, which raises this property; the view model never touches the application.
    /// </summary>
    public ThemePreference Theme => _config.Theme;

    [ObservableProperty]
    private OperationResultViewModel? _attachmentResult;

    public bool HasAttachmentResult => AttachmentResult is not null;

    partial void OnAttachmentResultChanged(OperationResultViewModel? value) =>
        OnPropertyChanged(nameof(HasAttachmentResult));

    public void DismissAttachmentResult() => AttachmentResult = null;

    [ObservableProperty]
    private bool _isReady;

    public string SaveStateText => Localizer.T(_saveState switch
    {
        SaveState.Saving => "save.saving",
        SaveState.Unsaved => "save.unsaved",
        SaveState.Error => "save.failed",
        _ => "save.saved",
    });

    // The save state the status-bar dot shows; the view maps it to theme brushes (saved is the
    // absence of the other three).
    [ObservableProperty]
    private bool _isSaveStateSaving;

    [ObservableProperty]
    private bool _isSaveStateUnsaved;

    [ObservableProperty]
    private bool _isSaveStateError;

    /// <summary>Status-bar text when a binder is open but no note is selected: the binder's note count.</summary>
    public string BinderStatusText => _binderStatus is null ? string.Empty : Localizer.Of(_binderStatus);

    private Message? _binderStatus;

    [ObservableProperty]
    private bool _hasBinder;

    public string BindersEmptyStateText =>
        Words(MainWindowEmptyStates.Binders(_allBinders.Count, Binders.Count, BindersFilter));

    public string NotesEmptyStateText =>
        Words(MainWindowEmptyStates.Notes(HasBinder, _allNotes.Count, Notes.Count, NotesFilter));

    public string AttachmentsEmptyStateText =>
        Words(MainWindowEmptyStates.Attachments(SelectedNote is not null, Attachments.Count));

    private static string Words(string? key) => key is null ? string.Empty : Localizer.T(key);

    /// <summary>True while files are being dragged over the attachments pane (drives the drop highlight).</summary>
    [ObservableProperty]
    private bool _isAttachmentDropActive;

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
    private double _attachmentsPaneWidth = 260;

    public int? WindowPositionX => _state.WindowPositionX;
    public int? WindowPositionY => _state.WindowPositionY;
    public double? WindowWidth => _state.WindowWidth;
    public double? WindowHeight => _state.WindowHeight;
    public bool WindowMaximized => _state.WindowMaximized;

    public void CaptureWindowPlacement(int x, int y, double width, double height, bool maximized)
    {
        _state.WindowPositionX = x;
        _state.WindowPositionY = y;
        _state.WindowWidth = width;
        _state.WindowHeight = height;
        _state.WindowMaximized = maximized;
    }

    // The records window keeps its layout in this app's state; each save writes state.json at once,
    // as an accepted close or a finished drag happens rarely.
    public double RecordsListWidth => _state.RecordsListWidth;

    public void SaveRecordsListWidth(double width)
    {
        _state.RecordsListWidth = width;
        PersistState();
    }

    public WindowPlacement? RecordsWindowPlacement =>
        _state is { RecordsWindowPositionX: { } x, RecordsWindowPositionY: { } y, RecordsWindowWidth: { } width, RecordsWindowHeight: { } height }
            ? new WindowPlacement(x, y, width, height, _state.RecordsWindowMaximized)
            : null;

    public void SaveRecordsWindowPlacement(WindowPlacement placement)
    {
        _state.RecordsWindowPositionX = placement.X;
        _state.RecordsWindowPositionY = placement.Y;
        _state.RecordsWindowWidth = placement.Width;
        _state.RecordsWindowHeight = placement.Height;
        _state.RecordsWindowMaximized = placement.Maximized;
        PersistState();
    }

    public TimeZoneInfo DisplayZone => _displayZone;

    /// <summary>The app's logger, which the windows this one opens log through.</summary>
    internal IAppLogger Log => _log;

    [ObservableProperty]
    private FontFamily _editorFontFamily = UiFont.ResolveEditor(EditorTextStyle.DefaultFixedWidthFamilies);

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
        // Material recovery is reported once the window can own the dialog (store-recovery-conventions).
        var quarantined = QuarantineJournal.Drain();
        foreach (var path in quarantined)
        {
            _log.Warn("Unreadable file set aside", new { path });
        }

        if (_configLoadError is { } configError)
        {
            await _dialogs.ShowErrorAsync(
                Message.Of("failure.startupDataTitle"),
                FailurePresentation.StartupSettings(_paths.ConfigFile, configError));
            return;
        }

        if (FailurePresentation.SetAsideConfig(quarantined, _paths.ConfigFile) is { } configCopy)
        {
            await _dialogs.ShowErrorAsync(
                Message.Of("quarantine.settingsTitle"),
                FailurePresentation.SettingsReset(configCopy));
        }

        if (!string.IsNullOrEmpty(_state.CurrentBinderPath)
            && _config.Binders.Any(binder => PathKey.Equal(binder.Path, _state.CurrentBinderPath))
            && File.Exists(_state.CurrentBinderPath))
        {
            await OpenBinderPathAsync(_state.CurrentBinderPath!, isNew: false, selectNoteId: _state.CurrentNoteId);
        }

        _externalTimer.Start();
    }

    /// <summary>Flushes pending work on shutdown. Pane widths are captured first by the view.</summary>
    /// <summary>
    /// Flushes and closes the open binder on quit. Returns false when the final flush failed — the
    /// binder stays open (autosave still retrying) so the caller can keep the window open rather than
    /// let unsaved edits vanish on quit.
    /// </summary>
    public async Task<bool> ShutdownAsync()
    {
        _log.Info("Application shutting down", new { path = _current?.Path });

        // An open still reading its file is superseded, as a later open would supersede it: its
        // result must not adopt a binder or write state after the app has shut down.
        _openGeneration++;

        // Persist state (including the current note id) while the binder is still open;
        // CloseCurrentAsync clears the selection, which would otherwise null out CurrentNoteId.
        PersistState();
        if (!await CloseCurrentAsync(clearSelection: false))
        {
            return false;
        }

        _externalTimer.Stop();
        _autosaveTimer.Stop();
        return true;
    }

    /// <summary>
    /// Raised after <see cref="NewNote"/> creates and selects a note, so the view can move keyboard
    /// focus into the title for immediate typing — a view concern the view model can't reach directly.
    /// </summary>
    public event EventHandler? NoteCreated;

    // ----- Commands ------------------------------------------------------------------------------

    [RelayCommand]
    private async Task NewBinder()
    {
        if (!IsReady)
        {
            return;
        }

        string? path;
        try
        {
            path = await _dialogs.PickBinderToCreateAsync();
            ResolveShellResult(NewBinderPickerResultKey);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open the new-binder picker", error: ex);
            ShowResult(
                OperationResultKind.Error,
                FailurePresentation.NewBinderPicker(ex),
                resultKey: NewBinderPickerResultKey);
            return;
        }
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

        string? path;
        try
        {
            path = await _dialogs.PickBinderToOpenAsync();
            ResolveShellResult(OpenBinderPickerResultKey);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open the binder picker", error: ex);
            ShowResult(
                OperationResultKind.Error,
                FailurePresentation.OpenBinderPicker(ex),
                resultKey: OpenBinderPickerResultKey);
            return;
        }
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
            // The row owns this: it is the narrowest surviving owner, it already shows the path,
            // and several missing binders each say it once instead of replacing one shared card.
            item.IsMissing = true;
            _log.Warn("Known binder is missing from disk", new { path = item.Path });
            return;
        }

        // The file is there, so the row stops saying it is gone: a marker of what is true has to
        // stop being shown the moment it stops being true.
        item.IsMissing = false;
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
        if (await CloseCurrentAsync(clearSelection: true))
        {
            ForgetBinder(path);
        }
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
            if (!await CloseCurrentAsync(clearSelection: true))
            {
                // The open binder's edits couldn't be flushed; keep it rather than forget it.
                return;
            }
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

        var now = _clock.GetUtcNow();
        var note = new Note
        {
            Id = IdGenerator.NewUnique(_current.Binder.Notes.Select(n => n.Id)),
            Title = string.Empty,
            Created = now,
            Modified = now,
            Body = string.Empty,
        };

        _current.Binder.Notes.Add(note);
        _notesChangedAt = now;
        // The empty note is its own baseline: until something is typed, its Modified stays its Created.
        _saved[note.Id] = SavedNote.Of(note);
        _lastContent[note.Id] = NoteContent.Of(note);
        // A brand-new note is the newest, so it goes to the top of the newest-first list.
        _allNotes.Insert(0, new NoteListItemViewModel(note, _displayZone));
        NotesFilter = string.Empty;
        RebuildNotes();
        SelectedNote = Notes.FirstOrDefault(n => n.Note.Id == note.Id);
        MarkDirty(note.Id);
        _log.Info("Created note", new { noteId = note.Id });
        NoteCreated?.Invoke(this, EventArgs.Empty);
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
        var question = string.IsNullOrWhiteSpace(note.Title)
            ? Message.Of("note.deleteUntitled")
            : Message.Of("note.deleteNamed", ("title", note.Title));
        var attachmentCount = note.Attachments.Count;
        var message = attachmentCount == 0
            ? question
            : Message.Join("note.deleteJoin",
            [
                question,
                // One attachment is its own sentence, not the one-form of a count: the plural's one form
                // also covers 21, 31 and more in some languages.
                attachmentCount == 1
                    ? Message.Of("note.deleteAttachment")
                    : Message.Of("note.deleteAttachments", ("count", attachmentCount)),
            ]);
        if (!await _dialogs.ConfirmAsync(
                Message.Of("note.deleteTitle"),
                message,
                "common.delete",
                destructive: true))
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
        var binder = _current;

        _current.Binder.Notes.Remove(note);
        _notesChangedAt = _clock.GetUtcNow();
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

        // The files go only once the binder on disk no longer refers to them, so a save that fails
        // never leaves a reopened binder pointing at deleted files. A binder reloaded or closed
        // meanwhile may still hold the note, so its files stay.
        var saved = await SaveCurrentAsync();
        if (ReferenceEquals(_current, binder))
        {
            DeleteNoteAssets(binder.Path, note, saved);
        }
    }

    /// <summary>
    /// Deletes the note's own attachment folder once its removal is saved. The folder is named by the
    /// note's id and nothing else reads it, so leaving it would leave bytes on disk under a name no one
    /// can resolve once the note is gone. Files left behind are reported, whether the save failed or the
    /// delete did, because the user asked for those files to go.
    /// </summary>
    private void DeleteNoteAssets(string binderPath, Note note, bool removalSaved)
    {
        var directory = BinderStore.NoteAssetsDirectory(binderPath, note.Id);
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            if (!removalSaved)
            {
                _log.Warn("Kept a deleted note's attachments: its removal was not saved", new { noteId = note.Id, path = directory });
                ShowResult(OperationResultKind.Warning, Message.Of("note.attachmentsLeft"), AttachmentCleanupResultKey);
                return;
            }

            _deleteDirectory(directory);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to delete a note's attachments", new { noteId = note.Id, path = directory }, ex);
            ShowResult(
                OperationResultKind.Warning,
                Message.Of("note.attachmentsLeft"),
                AttachmentCleanupResultKey);
        }
    }

    [RelayCommand]
    private async Task AddAttachment()
    {
        if (!IsReady || _current is null || SelectedNote is null || !CanEditNote)
        {
            return;
        }

        IReadOnlyList<string> files;
        try
        {
            files = await _dialogs.PickAttachmentsAsync();
            if (AttachmentResult?.ResultKey == AttachmentPickerResultKey)
            {
                AttachmentResult = null;
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open the attachment picker", new { noteId = SelectedNote.Note.Id }, ex);
            AttachmentResult = new OperationResultViewModel(
                OperationResultKind.Error,
                FailurePresentation.AttachmentPicker(ex),
                isPersistent: true,
                resultKey: AttachmentPickerResultKey);
            return;
        }

        await AddAttachmentFilesAsync(files);
    }

    /// <summary>Adds files dropped onto the attachments pane (same path as the Add button).</summary>
    public Task AddDroppedFiles(IReadOnlyList<string> files, int unavailable = 0) =>
        AddAttachmentFilesAsync(files, Math.Max(0, unavailable));

    /// <summary>
    /// Live reorder step during a drag: moves <paramref name="item"/> to <paramref name="newIndex"/> in
    /// the visible list only (no persistence). The order is committed once on release via
    /// <see cref="CommitAttachmentOrder"/>.
    /// </summary>
    public bool MoveAttachment(AttachmentItemViewModel item, int newIndex)
    {
        var oldIndex = Attachments.IndexOf(item);
        if (!CanEditNote || oldIndex < 0 || newIndex < 0 || newIndex >= Attachments.Count || newIndex == oldIndex)
        {
            return false;
        }

        Attachments.Move(oldIndex, newIndex);
        return true;
    }

    /// <summary>
    /// Cancels a live reorder by restoring the exact item objects captured at drag start. Returns
    /// false when the visible list was replaced or structurally changed in the meantime; the caller
    /// can then explicitly commit the current list so rendered and durable order still agree.
    /// </summary>
    public bool RestoreAttachmentOrder(IReadOnlyList<AttachmentItemViewModel> startingOrder)
    {
        if (startingOrder.Count != Attachments.Count
            || startingOrder.Any(start => !Attachments.Any(current => ReferenceEquals(current, start))))
        {
            return false;
        }

        for (var index = 0; index < startingOrder.Count; index++)
        {
            MoveAttachment(startingOrder[index], index);
        }

        return true;
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

    private async Task AddAttachmentFilesAsync(IReadOnlyList<string> files, int unavailable = 0)
    {
        if (!IsReady || _current is null || SelectedNote is null || !CanEditNote)
        {
            return;
        }

        var note = SelectedNote.Note;
        if (!IsLiveNote(note))
        {
            return;
        }

        if (files.Count == 0)
        {
            if (unavailable > 0)
            {
                _log.Warn("Attachment admission contained unreadable items", new { noteId = note.Id, unavailable });
                AttachmentResult = new OperationResultViewModel(
                    OperationResultKind.Warning,
                    unavailable == 1
                        ? Message.Of("attachments.unreadableThat")
                        : Message.Of("attachments.unreadable", ("count", unavailable)),
                    isPersistent: true);
            }
            return;
        }

        var directory = BinderStore.NoteAssetsDirectory(_current.Path, note.Id);
        // A snapshot of the note's current attachment names, taken on the UI thread: the background
        // step below only reads the filesystem and this list, and never touches the (UI-owned) note
        // object, so nothing races a concurrent edit while the batch is hashed and copied.
        var existingAttachmentNames = note.Attachments.ToList();
        var noteId = note.Id;

        _log.Info("Adding attachments", new { noteId, requested = files.Count });

        AttachmentImportOutcome outcome;
        try
        {
            outcome = await Task.Run(
                () => ImportAttachments(directory, existingAttachmentNames, noteId, files));
        }
        catch (Exception ex)
        {
            // A binder can live on a read-only, disconnected, or otherwise unavailable volume.
            // Attachment setup is an edge failure, not a reason for a file drop to escape into the
            // UI event loop and terminate the app.
            _log.Error("Failed to prepare attachment directory", new { noteId, path = directory }, ex);
            AttachmentResult = new OperationResultViewModel(
                OperationResultKind.Error,
                Message.Of("attachments.folderFailed"),
                isPersistent: true);
            return;
        }

        // The binder may have been reloaded or closed while the batch was hashed and copied in the
        // background. The files already landed safely under the note's own id either way; there is
        // just no live note left to attach them to, so the UI update is dropped rather than mutating
        // a stale object (same rule CheckExternalChangeAsync/RemoveAttachment follow for a late result).
        if (!IsLiveNote(note))
        {
            _log.Info("Discarding attachment-add result: note is no longer live", new { noteId });
            return;
        }

        foreach (var name in outcome.AddedNames)
        {
            note.Attachments.Add(name);
        }

        var added = outcome.AddedNames.Count;
        var duplicateNames = outcome.DuplicateNames;
        var failures = outcome.FailedNames;

        _log.Info("Attachments added", new
        {
            noteId,
            added,
            duplicates = duplicateNames.Count,
            unavailable,
            failed = failures.Count,
        });
        if (unavailable > 0)
        {
            _log.Warn("Attachment admission contained unreadable items", new { noteId, unavailable });
        }

        // Each part is a whole sentence with its own count, and the parts join through one catalogue
        // entry, so every language keeps its own plural forms and its own sentence spacing.
        var parts = new List<Message>();
        if (added > 0)
        {
            parts.Add(Message.Of("attachments.added", ("count", added)));
        }

        if (duplicateNames.Count > 0)
        {
            parts.Add(Message.Of("attachments.duplicates", ("names", SummarizeFileNames(duplicateNames))));
        }

        if (unavailable > 0)
        {
            parts.Add(Message.Of("attachments.unreadable", ("count", unavailable)));
        }

        if (failures.Count > 0)
        {
            parts.Add(Message.Of("attachments.failed", ("names", SummarizeFileNames(failures))));
            AttachmentResult = new OperationResultViewModel(
                OperationResultKind.Error,
                Message.Join("attachments.join", parts),
                isPersistent: true);
        }
        else if (unavailable > 0 || duplicateNames.Count > 0)
        {
            AttachmentResult = new OperationResultViewModel(
                unavailable > 0 ? OperationResultKind.Warning : OperationResultKind.Info,
                Message.Join("attachments.join", parts),
                isPersistent: true);
        }
        else
        {
            AttachmentResult = null;
        }

        if (added > 0)
        {
            LoadAttachments(note);
            MarkDirty(note.Id);
        }
    }

    private readonly record struct AttachmentImportOutcome(
        IReadOnlyList<string> AddedNames,
        IReadOnlyList<string> DuplicateNames,
        IReadOnlyList<string> FailedNames);

    /// <summary>
    /// The background half of an attachment add: prepares the note's assets directory, then hashes and
    /// copies each source file into it. Pure with respect to view-model state — it reads only its
    /// parameters and the filesystem, and returns what happened rather than mutating the note directly,
    /// so it is safe to run off the UI thread while the note keeps being edited. Directory creation is
    /// deliberately not wrapped in its own try/catch: that failure is distinct (nothing could be
    /// attempted at all) and is handled by the caller.
    /// </summary>
    private AttachmentImportOutcome ImportAttachments(
        string directory, IReadOnlyList<string> existingAttachmentNames, string noteId, IReadOnlyList<string> sources)
    {
        Directory.CreateDirectory(directory);

        // Hash the note's current attachments so a file whose content the note already has is not
        // copied again; a later identical file in the same batch dedups against earlier ones too.
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var existing in existingAttachmentNames)
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
                _log.Warn("Could not hash existing attachment for dedup", new { noteId, file = existing }, ex);
            }
        }

        var addedNames = new List<string>();
        var duplicateNames = new List<string>();
        var failures = new List<string>();
        foreach (var source in sources)
        {
            try
            {
                var hash = ContentHash.Sha256HexFile(source);
                if (hashes.ContainsKey(hash))
                {
                    duplicateNames.Add(Path.GetFileName(source));
                    continue;
                }

                var existingEntries = Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName)!;
                var name = UniqueFileName.Pick(existingEntries!, Path.GetFileName(source));
                // not recorded: attachments are copied binary content; the binder text records the
                // durable attachment reference, while binary writes stay outside the text history.
                File.Copy(source, Path.Combine(directory, name));
                addedNames.Add(name);
                hashes[hash] = name;
            }
            catch (Exception ex)
            {
                _log.Error("Failed to add attachment", new { noteId, source }, ex);
                failures.Add(Path.GetFileName(source));
            }
        }

        return new AttachmentImportOutcome(addedNames, duplicateNames, failures);
    }

    // The first few names, joined through the catalogue's list entry, and a count of the rest. The
    // answer is a value for a placeholder: a file name as it is, or a message holding several.
    private static object SummarizeFileNames(IEnumerable<string> fileNames)
    {
        const int visibleLimit = 3;
        var names = fileNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        if (names.Length == 0)
        {
            return Message.Of("files.selected");
        }

        var visible = JoinNames(names.Take(visibleLimit).ToArray());
        return names.Length > visibleLimit
            ? Message.Of("files.andMore", ("names", visible), ("count", names.Length - visibleLimit))
            : visible;
    }

    private static object JoinNames(IReadOnlyList<string> names) => names.Count == 1
        ? names[0]
        : Message.Of("files.join", ("first", names[0]), ("rest", JoinNames(names.Skip(1).ToArray())));

    [RelayCommand]
    private async Task RemoveAttachment(AttachmentItemViewModel item)
    {
        if (!IsReady || _current is null || SelectedNote is null || !CanEditNote)
        {
            return;
        }

        var note = SelectedNote.Note;
        if (!await _dialogs.ConfirmAsync(
                Message.Of("attachments.removeTitle"),
                Message.Of("attachments.removeMessage", ("name", item.FileName)),
                "common.remove",
                destructive: true))
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
        var binder = _current;
        var fileName = item.FileName;
        var fullPath = item.FullPath;
        var resultKey = $"remove-attachment:{note.Id}:{fileName}";
        note.Attachments.Remove(fileName);
        if (AttachmentResult?.ResultKey == resultKey)
        {
            AttachmentResult = null;
        }
        LoadAttachments(note);
        MarkDirty(note.Id);

        // The file goes only once the binder on disk no longer refers to it, so a save that fails never
        // leaves a reopened binder pointing at a deleted file. A binder reloaded or closed meanwhile may
        // still refer to it, so it stays.
        var saved = await SaveCurrentAsync();
        if (!ReferenceEquals(_current, binder) || !File.Exists(fullPath))
        {
            return;
        }

        if (!saved)
        {
            _log.Warn("Kept a removed attachment's file: its removal was not saved", new { noteId = note.Id, path = fullPath });
            ShowFileLeft();
            return;
        }

        try
        {
            _deleteFile(fullPath);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to delete attachment", new { noteId = note.Id, path = fullPath }, ex);
            ShowFileLeft();
        }

        void ShowFileLeft() => AttachmentResult = new OperationResultViewModel(
            OperationResultKind.Warning,
            Message.Of("attachments.removeFailed"),
            isPersistent: true,
            resultKey: resultKey);
    }

    [RelayCommand]
    private async Task OpenAttachment(AttachmentItemViewModel item)
    {
        if (!File.Exists(item.FullPath))
        {
            _log.Warn("Could not open unavailable attachment", new { path = item.FullPath });
            item.ShowUnavailable();
            return;
        }

        _log.Info("Opening attachment externally", new { file = item.FileName });
        try
        {
            await _dialogs.OpenPathExternallyAsync(item.FullPath);
            item.ClearOpenResult();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open attachment externally", new { path = item.FullPath }, ex);
            item.ShowOpenFailure();
        }
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        if (!IsReady)
        {
            return;
        }

        var draft = _config.CopySettings();
        if (!await _dialogs.ShowSettingsAsync(draft, candidate =>
            {
                var next = _config.Copy();
                next.TakeSettings(candidate);
                if (!TrySaveConfig(next))
                {
                    return false;
                }

                _log.Info("Settings saved", ConfigSummary(next));
                return true;
            }))
        {
            return;
        }

        var themeChanged = _config.Theme != draft.Theme;
        _config.TakeSettings(draft);
        ApplyConfig();
        if (themeChanged)
        {
            OnPropertyChanged(nameof(Theme));
        }

        foreach (var note in _allNotes)
        {
            note.Refresh(_displayZone);
        }

        // Last, once disk agrees: a language change redraws everything already on screen.
        Localizer.Use(_config.Language, ComputerLanguages);
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

        var next = _config.Copy();
        var index = next.TextStyles.IndexOf(next.ResolveDefaultStyle()!);
        var nextIndex = (index + 1) % next.TextStyles.Count;
        for (var i = 0; i < next.TextStyles.Count; i++)
        {
            next.TextStyles[i].IsDefault = i == nextIndex;
        }

        // Saved before it is adopted, so a style that did not reach the settings file is not shown as set.
        // While the settings could not be loaded, nothing is saved and the style holds for this session.
        if (IsReady && !TrySaveConfig(next, "failure.textStyleSave"))
        {
            return;
        }

        _config = next;
        ApplyTextStyle();
        var label = TextStyleLabels.For(_config.TextStyles, Localizer.T("settings.noFontFamily"), Localizer.Current.Culture)[nextIndex];
        _log.Info("Cycled text style", new { style = label });

        _textStyleStatus = Message.Of("textStyle.applied", ("style", label));
        OnPropertyChanged(nameof(TextStyleStatusText));
        _textStyleStatusTimer.Stop();
        _textStyleStatusTimer.Start();
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

        if (!await CloseCurrentAsync(clearSelection: false))
        {
            // The current binder's edits couldn't be flushed; keep it open rather than switch away.
            return;
        }

        // Marks this open as the one in flight. Checked again after the background read/write returns:
        // if another OpenBinderPathAsync call started in the meantime, that later open wins and this
        // call's result is discarded rather than yanking the user back to a binder they moved on from.
        var generation = ++_openGeneration;

        LoadedBinder loaded;
        try
        {
            // The binder's own read (JSON parse) or write (new-binder create) is genuine file I/O, so it
            // runs off the UI thread; only building the empty Binder for a new file — trivial, in-memory —
            // stays here. The work shares the save lock with SaveCurrentAsync/CheckExternalChangeAsync/
            // ReloadFromDiskAsync so it can never touch this file while one of those still has its write
            // or read of it in flight.
            await _binderFileLock.WaitAsync();
            try
            {
                if (isNew)
                {
                    var now = _clock.GetUtcNow();
                    var binder = new Binder
                    {
                        Id = IdGenerator.New(),
                        Created = now,
                        Modified = now,
                    };
                    var saved = await Task.Run(() => _binderStore.Save(path, binder));
                    loaded = new LoadedBinder(binder, saved.Path, saved.ContentHash);
                }
                else
                {
                    loaded = await Task.Run(() => _binderStore.Load(path));
                }
            }
            finally
            {
                _binderFileLock.Release();
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to open binder", new { path }, ex);
            if (generation == _openGeneration)
            {
                // Only report the failure if the user is still waiting on this open; a later open
                // already superseded it, so this error is no longer about anything they're looking at.
                await _dialogs.ShowErrorAsync(Message.Of("binder.openFailedTitle"), FailurePresentation.OpenBinder(ex, TitleFor(path)));
            }

            return;
        }

        if (generation != _openGeneration)
        {
            // A later open has already started (or finished) while this one's file was being read;
            // that binder is what the user is looking at now, so this stale result is dropped.
            _log.Info("Discarding binder open: superseded by a later open", new { path });
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

    /// <summary>
    /// Flushes pending edits and tears down the open binder. Returns false (and leaves the binder open,
    /// dirty, with the autosave still retrying) when the flush failed — so a save error never costs the
    /// user their edits. Callers abort whatever they were doing (forget / switch / quit) on false.
    /// </summary>
    private async Task<bool> CloseCurrentAsync(bool clearSelection)
    {
        if (_current is null)
        {
            return true;
        }

        _log.Info("Closing binder", new { path = _current.Path });
        _autosaveTimer.Stop();
        if (_dirty && !await SaveCurrentAsync())
        {
            // The flush failed (a full disk, a volume that disconnected, a file briefly locked). Keep the
            // binder open with its unsaved edits and resume the autosave so it retries; the error state
            // stays visible. Discarding here is the one thing we must never do.
            _autosaveTimer.Start();
            return false;
        }

        TearDownCurrent(clearSelection);
        return true;
    }

    /// <summary>Forgets the open binder as it stands, writing nothing to its file.</summary>
    private void TearDownCurrent(bool clearSelection)
    {
        _autosaveTimer.Stop();
        _current = null;
        HasBinder = false;
        _dirty = false;
        _dirtyNoteIds.Clear();
        _saved.Clear();
        _lastContent.Clear();
        Editor.Load(null);
        _allNotes.Clear();
        Notes.Clear();
        DisposeAttachments();
        Attachments.Clear();
        SelectedNote = null;
        UpdateBinderStatus();
        SetSaveState(SaveState.Saved);
        // What the closed binder's file did on disk no longer concerns the open workspace.
        ResolveShellResult(BinderFileResultKey);

        if (clearSelection)
        {
            _state.CurrentBinderPath = null;
            _state.CurrentNoteId = null;
            PersistState();
        }
    }

    /// <summary>
    /// The open binder's file now records a newer format than this build reads. It is intact data this
    /// build cannot read, so the binder is closed without saving and its file is never written
    /// (store-recovery-conventions); edits not yet saved cannot go anywhere else.
    /// </summary>
    private void CloseNewerBinder(string path, Exception? error = null)
    {
        _log.Warn("Binder on disk is newer than this build; closed without saving", new { path, unsavedEdits = _dirty }, error);
        TearDownCurrent(clearSelection: false);
        ApplyBinderFilter();
        ShowResult(OperationResultKind.Error, FailurePresentation.NewerBinder(TitleFor(path)), resultKey: BinderFileResultKey);
    }

    /// <summary>
    /// Flushes pending edits to disk. Returns true when there is nothing to save or the save succeeds,
    /// and false when a save was attempted and failed. Callers that tear down or quit rely on this to
    /// avoid discarding edits that never reached disk.
    /// </summary>
    private async Task<bool> SaveCurrentAsync()
    {
        if (!IsReady || _current is null || !_dirty)
        {
            return true;
        }

        // A second call arriving while one save's background I/O is still in flight (the autosave tick
        // and a manual Ctrl+S can land back to back) queues here instead of starting an overlapping write
        // to the same file. Awaited before touching any save state, so the UI thread is never blocked —
        // it just yields until its turn.
        await _binderFileLock.WaitAsync();
        try
        {
            // Re-check: whoever held the lock before this call may have already flushed these very
            // edits (or the binder may have been closed while this call was queued).
            if (!IsReady || _current is null || !_dirty)
            {
                return true;
            }

            var stopwatch = Stopwatch.StartNew();
            SetSaveState(SaveState.Saving);
            var binder = _current.Binder;
            var path = _current.Path;
            _log.Info("Saving binder", new { path, noteCount = binder.Notes.Count });

            // Each changed note's Modified was set when it was edited; one whose content is back to what
            // was last saved keeps that save's Modified. What is written becomes the new baseline once the
            // write succeeds.
            var written = new Dictionary<string, SavedNote>();
            foreach (var id in _dirtyNoteIds)
            {
                var note = binder.Notes.FirstOrDefault(n => n.Id == id);
                if (note is null)
                {
                    continue;
                }

                ApplyContentModified(note, editTime: null);
                written[id] = SavedNote.Of(note);
            }

            binder.Modified = BinderContentModified(binder);
            var noteIds = binder.Notes.Select(note => note.Id).ToArray();

            // Serializing the binder to text is pure, in-memory work over the (UI-owned, mutable)
            // Binder object, so it stays on the UI thread; only the text — an immutable snapshot — and
            // the I/O that writes it (the atomic write, fsync, and backup insert) move to a background
            // thread, so a keystroke landing mid-save can never race a background reader of the binder.
            var text = BinderStore.Serialize(binder);
            var generationAtSnapshot = _dirtyGeneration;

            try
            {
                var saved = await Task.Run(() => _binderStore.SaveText(path, text));
                _baselineHash = saved.ContentHash;
                foreach (var (id, savedNote) in written)
                {
                    _saved[id] = savedNote;
                }

                _savedNoteIds = noteIds;
                _savedBinderModified = binder.Modified;

                _externalChangeAcknowledged = false;

                // Only clear the dirty flags when nothing edited the binder while this save's background
                // write was in flight. A newer edit already re-set them (MarkDirty), possibly re-adding a
                // note this save just wrote; the next save stamps it again only if its content changed. The
                // save-state dot follows the same check: a newer, still-unsaved edit means the true state
                // is Unsaved, not Saved, even though this write itself succeeded.
                var settled = _dirtyGeneration == generationAtSnapshot;
                if (settled)
                {
                    _dirty = false;
                    _dirtyNoteIds.Clear();
                }

                Editor.RefreshMetadata();
                RefreshSelectedListItem();
                SetSaveState(settled ? SaveState.Saved : SaveState.Unsaved);
                ResolveShellResult(SaveFailureResultKey);
                // The file now holds this version, which settles any deletion or reload problem.
                ResolveShellResult(BinderFileResultKey);
                _log.Info("Binder saved", new { path = saved.Path, chars = saved.Text.Length, durationMs = stopwatch.ElapsedMilliseconds });
                return true;
            }
            catch (NewerFormatException ex)
            {
                // A newer DayNote rewrote the file since the last check, so this save would replace it.
                CloseNewerBinder(path, ex);
                return false;
            }
            catch (Exception ex)
            {
                // The write failed: _dirty and _dirtyNoteIds are untouched above, so the edit is still
                // considered unsaved and the autosave timer will retry it.
                _log.Error("Failed to save binder", new { path }, ex);
                SetSaveState(SaveState.Error);
                ShowResult(
                    OperationResultKind.Error,
                    FailurePresentation.SaveBinder(ex),
                    resultKey: SaveFailureResultKey);
                return false;
            }
        }
        finally
        {
            _binderFileLock.Release();
        }
    }

    internal async Task CheckExternalChangeAsync()
    {
        if (!IsReady || _current is null || _externalChangeAcknowledged)
        {
            return;
        }

        // A save is writing this file and has not yet recorded its new baseline, so the file would read
        // as changed by someone else; or an earlier check is still running. Skip this tick; the next one
        // compares against the baseline the holder leaves.
        if (!_binderFileLock.Wait(0))
        {
            return;
        }

        var checkedBinder = _current;
        try
        {
            // The whole-file read and SHA-256 hash are the slow part (not the property reads above),
            // so only that step moves to a background thread; the path and baseline are plain strings
            // captured on the UI thread, so there is nothing left for the background call to race.
            var path = checkedBinder.Path;
            var baselineHash = _baselineHash;
            var change = await Task.Run(() => _binderStore.CheckExternalChange(path, baselineHash));
            if (!ReferenceEquals(_current, checkedBinder))
            {
                // The binder was closed or switched while its file was being read.
                return;
            }

            switch (change)
            {
                case ExternalChange.None:
                    return;

                case ExternalChange.Deleted:
                    _externalChangeAcknowledged = true;
                    _log.Warn("Binder file was deleted on disk", new { path });
                    ShowResult(
                        OperationResultKind.Warning,
                        Message.Of("binder.deleted"),
                        resultKey: BinderFileResultKey);
                    return;

                case ExternalChange.Newer:
                    CloseNewerBinder(path);
                    return;

                case ExternalChange.Modified when !_dirty:
                    _log.Info("Binder changed on disk; reloading", new { path });
                    if (await ReloadFromDiskAsync(checkedBinder, yieldToNewEdits: true))
                    {
                        ShowResult(
                            OperationResultKind.Info,
                            Message.Of("binder.reloaded"),
                            resultKey: BinderFileResultKey);
                    }
                    return;

                case ExternalChange.Modified:
                    var choice = await _dialogs.AskExternalChangeAsync(TitleFor(path));
                    if (!ReferenceEquals(_current, checkedBinder))
                    {
                        return;
                    }

                    if (choice == ExternalChangeChoice.ReloadFromDisk)
                    {
                        _log.Info("External change: reloading from disk, discarding local edits", new { path });
                        await ReloadFromDiskAsync(checkedBinder, yieldToNewEdits: false);
                    }
                    else
                    {
                        // Keep the in-memory edits: re-baseline to the current on-disk content so the
                        // next save overwrites it, and so any *further* external change is still
                        // detected rather than silently suppressed.
                        _log.Info("External change: keeping local edits", new { path });
                        var diskHash = await Task.Run(() => _binderStore.ComputeHash(path));
                        if (ReferenceEquals(_current, checkedBinder))
                        {
                            _baselineHash = diskHash;
                        }
                    }

                    return;
            }
        }
        catch (Exception ex)
        {
            // A transient read failure during polling (the file briefly locked by a sync client,
            // antivirus, or an external editor) must not crash the app; skip this tick and retry.
            _log.Debug("External-change check skipped", new { path = checkedBinder.Path }, ex);
        }
        finally
        {
            _binderFileLock.Release();
        }
    }

    /// <summary>
    /// Reads the binder file on a background thread and adopts it only if the same binder is still open.
    /// With <paramref name="yieldToNewEdits"/>, an edit typed while the file was being read also cancels
    /// the reload, so the next check asks about it instead of silently dropping it.
    /// </summary>
    private async Task<bool> ReloadFromDiskAsync(LoadedBinder reloading, bool yieldToNewEdits)
    {
        var path = reloading.Path;
        var generation = _dirtyGeneration;
        LoadedBinder loaded;
        try
        {
            loaded = await Task.Run(() => _binderStore.Load(path));
        }
        catch (NewerFormatException ex)
        {
            // Rewritten by a newer DayNote since the check read it.
            if (ReferenceEquals(_current, reloading))
            {
                CloseNewerBinder(path, ex);
            }

            return false;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to reload binder", new { path }, ex);
            if (ReferenceEquals(_current, reloading))
            {
                ShowResult(
                    OperationResultKind.Error,
                    FailurePresentation.ReloadBinder(ex),
                    resultKey: BinderFileResultKey);
            }

            return false;
        }

        if (!ReferenceEquals(_current, reloading) || (yieldToNewEdits && _dirtyGeneration != generation))
        {
            return false;
        }

        AdoptLoaded(loaded, SelectedNote?.Note.Id);
        ResolveShellResult(BinderFileResultKey);
        return true;
    }

    // ----- View-model plumbing -------------------------------------------------------------------

    private void AdoptLoaded(LoadedBinder loaded, string? selectNoteId)
    {
        _current = loaded;
        _baselineHash = loaded.ContentHash;
        _externalChangeAcknowledged = false;
        _dirty = false;
        _dirtyNoteIds.Clear();
        _saved.Clear();
        _lastContent.Clear();
        foreach (var note in loaded.Binder.Notes)
        {
            _saved[note.Id] = SavedNote.Of(note);
            _lastContent[note.Id] = NoteContent.Of(note);
        }

        _savedNoteIds = loaded.Binder.Notes.Select(note => note.Id).ToArray();
        _savedBinderModified = loaded.Binder.Modified;

        HasBinder = true;
        BuildNotes(loaded.Binder, selectNoteId);
        UpdateBinderStatus();
        SetSaveState(SaveState.Saved);
    }

    private void BuildNotes(Binder binder, string? selectNoteId)
    {
        _allNotes.Clear();
        // Newest first (by creation time). Sorting by Created, not Modified, keeps the order stable
        // while editing; the stored file order is left untouched.
        foreach (var note in binder.Notes.OrderByDescending(n => n.Created))
        {
            _allNotes.Add(new NoteListItemViewModel(note, _displayZone));
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

    /// <summary>Rebuilds the master binders list from config, then applies the current filter.</summary>
    private void RebuildBinders()
    {
        _allBinders.Clear();
        foreach (var entry in _config.Binders)
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
    /// The binder's display title: its stored title from app config, or the file name when no
    /// title is stored. Titles live in config (not the .daynote file), so this needs no file I/O.
    /// </summary>
    private string TitleFor(string path)
    {
        var title = _config.Binders.FirstOrDefault(b => PathKey.Equal(b.Path, path))?.Title;
        return string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title;
    }

    /// <summary>
    /// Applies an inline title edit to a binder. The title is a label stored in app config (never
    /// in the .daynote file), so this just updates that config entry and persists. A blank or unchanged
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

        var next = _config.Copy();
        var entry = next.Binders.FirstOrDefault(b => PathKey.Equal(b.Path, item.Path));
        if (entry is null)
        {
            return; // not a known binder (shouldn't happen for a visible row)
        }

        entry.Title = newTitle;
        if (!TrySaveConfig(next, "failure.binderRenameSave"))
        {
            return;
        }

        _config = next;
        item.Title = newTitle;
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
    /// Live reorder step: moves <paramref name="item"/> to <paramref name="target"/>'s place in the
    /// master list and re-applies the filter, so a filtered view moves past hidden rows by visible
    /// neighbour. Nothing is persisted until <see cref="CommitBinderOrder"/>.
    /// </summary>
    public bool MoveBinder(BinderListItemViewModel item, BinderListItemViewModel target)
    {
        var from = _allBinders.IndexOf(item);
        var to = _allBinders.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return false;
        }

        _allBinders.RemoveAt(from);
        _allBinders.Insert(to, item);
        ApplyBinderFilter();
        return true;
    }

    /// <summary>
    /// Re-reads which known binders are still on disk, so a file deleted or restored outside the app
    /// shows on its row within a tick: the row that wants removing says so without being opened
    /// first. The open binder is left out — while it is open, its file's fate is reported with the
    /// recovery that goes with it, and one condition is reported in one place.
    /// </summary>
    internal void RefreshKnownBinders()
    {
        foreach (var item in _allBinders)
        {
            var missing = _current is not null && PathKey.Equal(_current.Path, item.Path)
                ? false
                : !File.Exists(item.Path);
            if (item.IsMissing != missing)
            {
                item.IsMissing = missing;
            }
        }
    }

    /// <summary>The master binder order, hidden rows included, for restoring a cancelled drag.</summary>
    public IReadOnlyList<BinderListItemViewModel> BinderOrder() => _allBinders.ToArray();

    /// <summary>
    /// Restores a captured master order. Returns false when the known binders changed meanwhile, so the
    /// caller commits the current order instead of applying stale identities.
    /// </summary>
    public bool RestoreBinderOrder(IReadOnlyList<BinderListItemViewModel> order)
    {
        if (order.Count != _allBinders.Count || order.Any(item => !_allBinders.Contains(item)))
        {
            return false;
        }

        _allBinders.Clear();
        _allBinders.AddRange(order);
        ApplyBinderFilter();
        return true;
    }

    /// <summary>Persists the master binder order to app config once; a no-op when it is unchanged.</summary>
    public void CommitBinderOrder()
    {
        var order = _allBinders.Select(item => item.Path).ToList();
        if (_config.Binders.Select(entry => entry.Path).SequenceEqual(order))
        {
            return;
        }

        var next = _config.Copy();
        var reordered = _allBinders
            .Select(item => next.Binders.Find(entry => PathKey.Equal(entry.Path, item.Path)))
            .OfType<KnownBinder>()
            .ToList();
        reordered.AddRange(next.Binders.Except(reordered));
        next.Binders = reordered;
        if (!TrySaveConfig(next, "failure.binderOrderSave"))
        {
            // The rows go back to the order the settings file still holds.
            RebuildBinders();
            return;
        }

        _config = next;
        _log.Info("Reordered binders", new { count = order.Count });
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
        if (_config.Binders.Any(b => PathKey.Equal(b.Path, full)))
        {
            // Already known: don't churn the list (which is this ListBox's ItemsSource); just refresh
            // which row is marked current.
            ApplyBinderFilter();
            return;
        }

        var next = _config.Copy();
        next.Binders.Insert(0, new KnownBinder { Path = full, Title = Path.GetFileNameWithoutExtension(full) });
        // The binder stays open either way; only its place in the list depends on the save.
        if (!TrySaveConfig(next, "failure.binderAddSave", OperationResultKind.Warning))
        {
            ApplyBinderFilter();
            return;
        }

        _config = next;
        RebuildBinders();
    }

    // Removes a binder from the known list and persists. Caller closes it first if it is the open one.
    private void ForgetBinder(string path)
    {
        var next = _config.Copy();
        if (next.Binders.RemoveAll(b => PathKey.Equal(b.Path, path)) > 0)
        {
            if (!TrySaveConfig(next, "failure.binderForgetSave"))
            {
                RebuildBinders();
                return;
            }

            _config = next;
        }

        RebuildBinders();
        _log.Info("Forgot binder", new { path });
    }

    private void LoadAttachments(Note? note)
    {
        var canReuseRows = note is not null
            && string.Equals(_attachmentNoteId, note.Id, StringComparison.Ordinal);
        var reusable = canReuseRows
            ? Attachments.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, AttachmentItemViewModel>(StringComparer.OrdinalIgnoreCase);

        if (!canReuseRows)
        {
            DisposeAttachments();
        }

        Attachments.Clear();
        if (note is null || _current is null)
        {
            _attachmentNoteId = null;
            return;
        }

        foreach (var attachment in BinderStore.ResolveAttachments(_current.Path, note))
        {
            if (reusable.Remove(attachment.FileName, out var existing))
            {
                Attachments.Add(existing);
            }
            else
            {
                Attachments.Add(new AttachmentItemViewModel(attachment, _log));
            }
        }

        foreach (var removed in reusable.Values)
        {
            removed.Dispose();
        }

        _attachmentNoteId = note.Id;
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

    private void OnEditorChanged(object? sender, EventArgs e)
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
        _dirtyGeneration++;
        if (noteId is not null)
        {
            _dirtyNoteIds.Add(noteId);
            if (_current.Binder.Notes.FirstOrDefault(n => n.Id == noteId) is { } note)
            {
                ApplyContentModified(note, _clock.GetUtcNow());
            }
        }

        SetSaveState(SaveState.Unsaved);
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void RefreshSelectedListItem() => SelectedNote?.Refresh(_displayZone);

    private static readonly TimeSpan TransientResultLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TextStyleStatusLifetime = TimeSpan.FromSeconds(5);

    public void DismissResult(OperationResultViewModel result) => Results.Remove(result);

    /// <summary>
    /// Shows the current result for one subject. A subject holds at most one result: a later one
    /// replaces it where it stands, and a new subject goes on top. Information needs no action, so
    /// it clears itself; a warning or error stays until the user dismisses it or its owner resolves
    /// it, and a result for another subject never removes it.
    /// </summary>
    private void ShowResult(OperationResultKind kind, Message message, string resultKey)
    {
        var isPersistent = kind != OperationResultKind.Info;
        var existing = Results.FirstOrDefault(result => result.ResultKey == resultKey);
        if (isPersistent && existing is not null && existing.Kind == kind && existing.Text == Localizer.Of(message))
        {
            // The same unresolved problem again: it is already on screen and already announced.
            return;
        }

        var result = new OperationResultViewModel(kind, message, isPersistent, resultKey);
        AnnouncedResult = result;
        if (existing is null)
        {
            Results.Insert(0, result);
        }
        else
        {
            Results[Results.IndexOf(existing)] = result;
        }

        if (isPersistent)
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = TransientResultLifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Results.Remove(result);
        };
        timer.Start();
    }

    private void ResolveShellResult(string resultKey)
    {
        foreach (var result in Results.Where(result => result.ResultKey == resultKey).ToArray())
        {
            Results.Remove(result);
        }
    }

    partial void OnNotesFilterChanged(string value)
    {
        RebuildNotes();
        OnPropertyChanged(nameof(NotesEmptyStateText));
    }

    partial void OnBindersFilterChanged(string value)
    {
        ApplyBinderFilter();
        OnPropertyChanged(nameof(BindersEmptyStateText));
    }

    partial void OnHasBinderChanged(bool value) => OnPropertyChanged(nameof(NotesEmptyStateText));

    partial void OnSelectedNoteChanged(NoteListItemViewModel? value)
    {
        AttachmentResult = null;
        Editor.Load(value?.Note);
        LoadAttachments(value?.Note);
        OnPropertyChanged(nameof(AttachmentsEmptyStateText));
        OnPropertyChanged(nameof(CanEditNote));
        _state.CurrentNoteId = value?.Note.Id;
        UpdateBinderStatus();
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

    /// <summary>
    /// Persists a candidate configuration for a command that changes it outside Settings. On failure the
    /// command's own consequence, <paramref name="failureKey"/>, is shown and false is returned, so the
    /// command keeps the configuration it had.
    /// </summary>
    private bool TrySaveConfig(AppConfig config, string failureKey, OperationResultKind kind = OperationResultKind.Error)
    {
        if (TrySaveConfig(config))
        {
            return true;
        }

        ShowResult(kind, Message.Of(failureKey), ConfigSaveFailureResultKey);
        return false;
    }

    /// <summary>Persists the configuration; returns false (and logs) if the write fails.</summary>
    private bool TrySaveConfig(AppConfig config)
    {
        try
        {
            _configStore.Save(config);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save configuration", error: ex);
            return false;
        }
    }

    // Each store recovers on its own path (store-recovery-conventions): the settings halt, the
    // disposable view state is logged and replaced. A newer file of either is never written this session.
    private void LoadConfigAndState()
    {
        try
        {
            _paths.EnsureCreated();
            _config = _configStore.Load();
        }
        catch (Exception ex)
        {
            _configLoadError = ex;
            _config = new AppConfig();
            _log.Error("Failed to load configuration; saving disabled", new { path = _paths.ConfigFile }, ex);
            return;
        }

        try
        {
            _state = _stateStore.Load() ?? new AppState();
        }
        catch (Exception ex)
        {
            _state = new AppState();
            _log.Warn("Failed to load state; using defaults", new { path = _paths.StateFile }, ex);
        }

        _log.Info("Configuration and state loaded", ConfigSummary(_config));
    }

    private void ApplyConfig()
    {
        ApplyUiFont();
        ApplyTextStyle();
        _displayZone = DayNoteTime.DisplayZone(_config.TimeZone);
        Editor.SetTimeZone(_displayZone);
        _autosaveTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.25, _config.AutosaveDelaySeconds));
    }

    /// <summary>
    /// Applies the configured UI (chrome) font app-wide by overriding the <c>AppFontFamily</c> resource
    /// the Window style binds via DynamicResource. The editor body keeps its own text-style font.
    /// </summary>
    private void ApplyUiFont()
    {
        if (Application.Current is { } app)
        {
            app.Resources["AppFontFamily"] = UiFont.Resolve(_config.UiFontFamily);
        }
    }

    /// <summary>Applies the selected text-style preset (or the first available) to the editor properties.</summary>
    private void ApplyTextStyle()
    {
        var style = _config.ResolveDefaultStyle();
        if (style is null)
        {
            return;
        }

        EditorFontFamily = UiFont.ResolveEditor(style.FontFamily);
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
        OnPropertyChanged(nameof(SaveStateText));

        // The dot tracks the state by color: green saved, accent saving, amber unsaved, red failed.
        IsSaveStateSaving = _saveState == SaveState.Saving;
        IsSaveStateUnsaved = _saveState == SaveState.Unsaved;
        IsSaveStateError = _saveState == SaveState.Error;
    }

    /// <summary>
    /// The status bar's right side shows per-note metadata when a note is open; when a binder is open
    /// but nothing is selected, it shows the binder's note count instead, so the bar is never blank.
    /// </summary>
    private void UpdateBinderStatus()
    {
        var count = _current?.Binder.Notes.Count ?? 0;
        _binderStatus = _current is null || SelectedNote is not null ? null
            : count == 0 ? Message.Of("binder.noNotes")
            : Message.Of("binder.noteCount", ("count", count));
        OnPropertyChanged(nameof(BinderStatusText));
    }

    // ----- Helpers -------------------------------------------------------------------------------

    /// <summary>The key effective configuration, summarized for the log (no secret-bearing fields).</summary>
    private static object ConfigSummary(AppConfig config) => new
    {
        uiFontFamily = config.UiFontFamily,
        theme = config.Theme,
        defaultTextStyle = config.TextStyles.FindIndex(style => style.IsDefault),
        textStyleCount = config.TextStyles.Count,
        autosaveDelaySeconds = config.AutosaveDelaySeconds,
        timeZone = config.TimeZone,
        language = config.Language,
    };

    private static string EnsureDaynoteExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".daynote", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ".daynote";

    /// <summary>
    /// Sets a changed note's Modified: the saved time when its content is back to what was last saved,
    /// otherwise <paramref name="editTime"/> when this change altered the content. A change that leaves
    /// the content as it was, and a save (no edit time), touch nothing else.
    /// </summary>
    private void ApplyContentModified(Note note, DateTimeOffset? editTime)
    {
        var content = NoteContent.Of(note);
        if (_saved.TryGetValue(note.Id, out var saved) && saved.Content == content)
        {
            note.Modified = saved.Modified;
        }
        else if (editTime is { } time && (!_lastContent.TryGetValue(note.Id, out var last) || last != content))
        {
            note.Modified = time;
        }

        if (editTime is not null)
        {
            _lastContent[note.Id] = content;
        }
    }

    /// <summary>
    /// The binder's Modified: the latest edit among its notes whose content differs from what was saved,
    /// and the latest addition or deletion when its notes differ from those saved; the saved time when
    /// nothing differs. A note's Modified already holds the time of its own latest edit.
    /// </summary>
    private DateTimeOffset BinderContentModified(Binder binder)
    {
        DateTimeOffset? latest = binder.Notes.Select(note => note.Id).SequenceEqual(_savedNoteIds) ? null : _notesChangedAt;
        foreach (var note in binder.Notes)
        {
            if ((!_saved.TryGetValue(note.Id, out var saved) || saved.Content != NoteContent.Of(note))
                && (latest is null || note.Modified > latest))
            {
                latest = note.Modified;
            }
        }

        return latest ?? _savedBinderModified;
    }

    /// <summary>A note's content as the file stores it: what decides whether Modified moves.</summary>
    private readonly record struct NoteContent(string Title, string Body, string Attachments)
    {
        // Attachment names are bare file names, so '/' cannot occur inside one.
        public static NoteContent Of(Note note) => new(
            TextCleanup.SingleLine(note.Title),
            BodyCleanup.Normalize(note.Body),
            string.Join('/', note.Attachments));
    }

    /// <summary>A note's content and Modified time as last loaded or saved.</summary>
    private readonly record struct SavedNote(NoteContent Content, DateTimeOffset Modified)
    {
        public static SavedNote Of(Note note) => new(NoteContent.Of(note), note.Modified);
    }
}
