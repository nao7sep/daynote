using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.Core.Text;
using DayNote.Core.Time;

namespace DayNote.ViewModels;

/// <summary>A row in the notes pane: a note's title, lifecycle status, and its creation time.</summary>
public sealed partial class NoteListItemViewModel : ObservableObject
{
    // A label cap well above any pane width; CharacterEllipsis does the visual fit (per text-cleanup-conventions).
    private const int LabelLength = 80;

    public NoteListItemViewModel(Note note, TimeZoneInfo displayZone)
    {
        Note = note;
        Refresh(displayZone);
    }

    public Note Note { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    // The lifecycle state the row's stripe and label show; the view maps it to theme brushes.
    // Draft is the absence of the other three.
    [ObservableProperty]
    private bool _isStatusReady;

    [ObservableProperty]
    private bool _isStatusPublished;

    [ObservableProperty]
    private bool _isStatusExpired;

    /// <summary>
    /// Re-reads the title, status, and creation time from the underlying note. The owner passes the zone
    /// it displays times in now, rather than the row keeping the one it was built with, so a zone saved
    /// in Settings reaches every row.
    /// </summary>
    public void Refresh(TimeZoneInfo displayZone)
    {
        Title = DisplayLabel();
        // Show the creation time consistently (not modified) so the list order (newest-created first)
        // and the displayed date agree, and a row does not jump its label as it is edited.
        Subtitle = DayNoteTime.ToDisplay(Note.Created, displayZone);
        StatusLabel = StatusText(Note.Status);
        IsStatusReady = Note.Status == NoteStatus.Ready;
        IsStatusPublished = Note.Status == NoteStatus.Published;
        IsStatusExpired = Note.Status == NoteStatus.Expired;
    }

    private static string StatusText(NoteStatus status) => status switch
    {
        NoteStatus.Ready => "Ready",
        NoteStatus.Published => "Published",
        NoteStatus.Expired => "Expired",
        _ => "Draft",
    };

    /// <summary>
    /// The note's title, or — until one is set — a single-line preview of the body, so an untitled
    /// note is still recognizable in the list. Falls back to "(untitled)" only when the body is empty too.
    /// </summary>
    private string DisplayLabel()
    {
        if (!string.IsNullOrWhiteSpace(Note.Title))
        {
            return Note.Title;
        }

        var preview = TextCleanup.Truncate(Note.Body, LabelLength).Text;
        return string.IsNullOrEmpty(preview) ? "(untitled)" : preview;
    }
}
