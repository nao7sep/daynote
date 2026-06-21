using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.Core.Text;
using DayNote.Core.Time;

namespace DayNote.Desktop.ViewModels;

/// <summary>
/// The editor pane: the selected note's title, body, and metadata, plus live word, character, and
/// X-weighted counts. Edits are written straight back to the underlying <see cref="Note"/>; the
/// <see cref="Edited"/> event drives the main window's per-note dirty tracking and autosave debounce.
/// The body is plain text — markdown is not rendered.
/// </summary>
public sealed partial class EditorViewModel : ViewModelBase
{
    private string _displayTimeZone;
    private Note? _note;
    private bool _suppress;

    public EditorViewModel(string displayTimeZone)
    {
        _displayTimeZone = displayTimeZone;
        UpdateCounts();
    }

    /// <summary>Raised when the user edits the title or body of the loaded note.</summary>
    public event EventHandler? Edited;

    public Note? Note => _note;

    /// <summary>The lifecycle states offered by the editor's status picker, in workflow order.</summary>
    public IReadOnlyList<NoteStatus> StatusOptions { get; } = Enum.GetValues<NoteStatus>();

    [ObservableProperty]
    private bool _hasNote;

    [ObservableProperty]
    private NoteStatus _status = NoteStatus.Draft;

    /// <summary>
    /// Whether the title and body may be edited. Only a <see cref="NoteStatus.Draft"/> note is editable;
    /// checked / published / expired notes are immutable until moved back to draft (the status picker
    /// itself stays enabled so that move is always possible).
    /// </summary>
    [ObservableProperty]
    private bool _isEditable = true;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private string _createdText = string.Empty;

    [ObservableProperty]
    private string _modifiedText = string.Empty;

    [ObservableProperty]
    private string _wordsText = "0 words";

    [ObservableProperty]
    private string _charsText = "0 characters";

    [ObservableProperty]
    private string _xCountText = "X: 0/280";

    [ObservableProperty]
    private bool _isWithinXLimit = true;

    public void SetTimeZone(string timeZone)
    {
        _displayTimeZone = timeZone;
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
            Title = cleaned; // setter raises OnTitleChanged → writes the note and marks dirty
        }
    }

    /// <summary>Re-reads the created/modified metadata after a save updates the note's timestamps.</summary>
    public void RefreshMetadata()
    {
        if (_note is null)
        {
            CreatedText = string.Empty;
            ModifiedText = string.Empty;
            return;
        }

        CreatedText = "Created " + DayNoteTime.ToDisplay(_note.Created, _displayTimeZone);
        ModifiedText = "Modified " + DayNoteTime.ToDisplay(_note.Modified, _displayTimeZone);
    }

    partial void OnTitleChanged(string value)
    {
        if (_suppress || _note is null)
        {
            return;
        }

        _note.Title = value;
        Edited?.Invoke(this, EventArgs.Empty);
    }

    partial void OnStatusChanged(NoteStatus value)
    {
        // Reflect editability even while loading (so opening a non-draft note locks it immediately).
        IsEditable = value == NoteStatus.Draft;

        if (_suppress || _note is null)
        {
            return;
        }

        _note.Status = value;
        Edited?.Invoke(this, EventArgs.Empty);
    }

    partial void OnBodyChanged(string value)
    {
        UpdateCounts();
        if (_suppress || _note is null)
        {
            return;
        }

        _note.Body = value;
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCounts()
    {
        var counts = CharacterCount.Count(Body ?? string.Empty);
        WordsText = counts.Words == 1 ? "1 word" : $"{counts.Words} words";
        CharsText = counts.Chars == 1 ? "1 character" : $"{counts.Chars} characters";
        XCountText = $"X: {counts.XWeightedChars}/{counts.XLimit}";
        IsWithinXLimit = counts.XWithinLimit;
    }
}
