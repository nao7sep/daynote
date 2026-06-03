using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.Core.Time;

namespace DayNote.Desktop.ViewModels;

/// <summary>A row in the notes pane: shows a note's title and an at-a-glance modified time.</summary>
public sealed partial class NoteListItemViewModel : ObservableObject
{
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
        Title = string.IsNullOrWhiteSpace(Note.Title) ? "(untitled)" : Note.Title;
        Subtitle = DayNoteTime.ToDisplay(Note.Modified, _displayTimeZone);
    }
}
