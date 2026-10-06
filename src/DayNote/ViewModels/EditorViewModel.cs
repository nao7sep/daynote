using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.Core.Text;
using DayNote.Core.Time;
using DayNote.I18n;

namespace DayNote.ViewModels;

/// <summary>
/// The editor pane: the selected note's title, body, and metadata, plus live word, character, and
/// X-weighted counts. Changes are written straight back to the underlying <see cref="Note"/>; the
/// <see cref="Changed"/> event drives the main window's dirty tracking and autosave debounce.
/// The body is plain text — markdown is not rendered.
/// </summary>
public sealed partial class EditorViewModel : ViewModelBase
{
    private readonly TimeProvider _clock;
    private TimeZoneInfo _displayZone;
    private Note? _note;
    private bool _suppress;

    public EditorViewModel(TimeZoneInfo displayZone, TimeProvider? clock = null)
    {
        _displayZone = displayZone;
        _clock = clock ?? TimeProvider.System;
        UpdateCounts();
    }

    /// <summary>
    /// Raised when the loaded note changes in a way that must be saved: its title, body, status or
    /// lock. Whether its content changed, and so its Modified time, is decided by the save.
    /// </summary>
    public event EventHandler? Changed;

    public Note? Note => _note;

    /// <summary>The lifecycle states offered by the editor's status picker, in workflow order.</summary>
    public IReadOnlyList<NoteStatus> StatusOptions { get; } = Enum.GetValues<NoteStatus>();

    [ObservableProperty]
    private bool _hasNote;

    [ObservableProperty]
    private NoteStatus _status = NoteStatus.Draft;

    /// <summary>
    /// Whether the note's content is locked. A locked note's title, body and attachments cannot be
    /// edited; its status and deletion stay free, per the content-lifecycle-conventions.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _locked;

    /// <summary>Whether the title, body and attachments may be edited: the note is not locked.</summary>
    public bool IsEditable => !Locked;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private string _createdText = string.Empty;

    [ObservableProperty]
    private string _modifiedText = string.Empty;

    [ObservableProperty]
    private string _discardedAtText = string.Empty;

    [ObservableProperty]
    private string _verifiedAtText = string.Empty;

    [ObservableProperty]
    private string _publishedAtText = string.Empty;

    [ObservableProperty]
    private string _retiredAtText = string.Empty;

    [ObservableProperty]
    private string _wordsText = string.Empty;

    [ObservableProperty]
    private string _charsText = string.Empty;

    [ObservableProperty]
    private string _xCountText = string.Empty;

    [ObservableProperty]
    private bool _isWithinXLimit = true;

    public void SetTimeZone(TimeZoneInfo zone)
    {
        _displayZone = zone;
        RefreshMetadata();
    }

    /// <summary>Loads a note into the editor (or clears it when null) without raising edits.</summary>
    public void Load(Note? note)
    {
        _suppress = true;
        _note = note;
        HasNote = note is not null;
        Title = note?.Title ?? string.Empty;
        Status = note?.Status ?? NoteStatus.Draft;
        Locked = note?.Locked ?? false;
        Body = note?.Body ?? string.Empty;
        RefreshMetadata();
        UpdateCounts();
        _suppress = false;
    }

    /// <summary>
    /// Normalizes the title to a single line at a commit point (blur or submit) — never per keystroke,
    /// per the text-input-ime-conventions. No-op when nothing changes, so it does not spuriously dirty.
    /// </summary>
    public void NormalizeTitle()
    {
        if (_note is null)
        {
            return;
        }

        var cleaned = TextCleanup.SingleLine(Title);
        if (!string.Equals(cleaned, Title, StringComparison.Ordinal))
        {
            Title = cleaned; // setter raises OnTitleChanged → writes the note and raises Changed
        }
    }

    /// <summary>Re-reads the created/modified/lifecycle metadata after a save or status change.</summary>
    public void RefreshMetadata()
    {
        if (_note is null)
        {
            CreatedText = string.Empty;
            ModifiedText = string.Empty;
            DiscardedAtText = string.Empty;
            VerifiedAtText = string.Empty;
            PublishedAtText = string.Empty;
            RetiredAtText = string.Empty;
            return;
        }

        var now = _clock.GetUtcNow();
        string At(string key, DateTimeOffset? time) => time is { } value
            ? Localizer.T(key, ("time", DayNoteTime.ToSmartDisplay(value, _displayZone, Localizer.Current.Culture, now)))
            : string.Empty;

        CreatedText = At("meta.created", _note.Created);
        ModifiedText = At("meta.modified", _note.Modified);
        DiscardedAtText = At("meta.discarded", _note.DiscardedAt);
        VerifiedAtText = At("meta.verified", _note.VerifiedAt);
        PublishedAtText = At("meta.published", _note.PublishedAt);
        RetiredAtText = At("meta.retired", _note.RetiredAt);
    }

    /// <summary>
    /// Brings the metadata and counts into the current language. The main window's view model calls
    /// it when the language changes.
    /// </summary>
    internal void Retranslate()
    {
        RefreshMetadata();
        UpdateCounts();
    }

    partial void OnTitleChanged(string value)
    {
        if (_suppress || _note is null)
        {
            return;
        }

        _note.Title = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnStatusChanged(NoteStatus value)
    {
        if (_suppress || _note is null)
        {
            return;
        }

        NoteLifecycle.ApplyTransition(_note, value, _clock.GetUtcNow());
        RefreshMetadata();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnLockedChanged(bool value)
    {
        if (_suppress || _note is null)
        {
            return;
        }

        _note.Locked = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnBodyChanged(string value)
    {
        UpdateCounts();
        if (_suppress || _note is null)
        {
            return;
        }

        _note.Body = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCounts()
    {
        var counts = CharacterCount.Count(Body ?? string.Empty);
        WordsText = Localizer.T("counts.words", ("count", counts.Words));
        CharsText = Localizer.T("counts.chars", ("count", counts.Chars));
        XCountText = Localizer.T("counts.x", ("count", counts.XWeightedChars), ("limit", counts.XLimit));
        IsWithinXLimit = counts.XWithinLimit;
    }
}
