using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.Core.Text;
using DayNote.Core.Time;

namespace DayNote.Desktop.ViewModels;

/// <summary>A row in the notes pane: shows a note's title and an at-a-glance modified time.</summary>
public sealed partial class NoteListItemViewModel : ObservableObject
{
    // A label cap well above any pane width; CharacterEllipsis does the visual fit (per text-cleanup-conventions).
    private const int LabelLength = 80;

    private readonly string _displayTimeZone;

    public NoteListItemViewModel(Note note, string displayTimeZone)
    {
        Note = note;
        _displayTimeZone = displayTimeZone;
        Refresh();
    }

    public Note Note { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>Re-reads the title and modified time from the underlying note.</summary>
    public void Refresh()
    {
        Title = DisplayLabel();
        Subtitle = DayNoteTime.ToDisplay(Note.Modified, _displayTimeZone);
    }

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
