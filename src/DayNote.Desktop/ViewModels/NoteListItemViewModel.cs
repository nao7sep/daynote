using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.Core.Models;
using DayNote.Core.Text;
using DayNote.Core.Time;

namespace DayNote.Desktop.ViewModels;

/// <summary>A row in the notes pane: a note's title, lifecycle status, and its creation time.</summary>
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

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    [ObservableProperty]
    private IBrush _statusBrush = Brushes.Gray;

    /// <summary>Re-reads the title, status, and creation time from the underlying note.</summary>
    public void Refresh()
    {
        Title = DisplayLabel();
        // Show the creation time consistently (not modified) so the list order (newest-created first)
        // and the displayed date agree, and a row does not jump its label as it is edited.
        Subtitle = DayNoteTime.ToDisplay(Note.Created, _displayTimeZone);
        StatusLabel = StatusText(Note.Status);
        StatusBrush = StatusColor(Note.Status);
    }

    private static string StatusText(NoteStatus status) => status switch
    {
        NoteStatus.Ready => "Ready",
        NoteStatus.Published => "Published",
        NoteStatus.Expired => "Expired",
        _ => "Draft",
    };

    private static IBrush StatusColor(NoteStatus status)
    {
        var key = status switch
        {
            NoteStatus.Ready => "StatusReadyBrush",
            NoteStatus.Published => "StatusPublishedBrush",
            NoteStatus.Expired => "StatusExpiredBrush",
            _ => "StatusDraftBrush",
        };

        return PaletteBrush.Resolve(key, Brushes.Gray);
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
