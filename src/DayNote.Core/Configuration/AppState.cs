namespace DayNote.Core.Configuration;

/// <summary>
/// Volatile session state, persisted to <c>~/.daynote/state.json</c>: window geometry, pane
/// widths, the recent-notebooks list, and the current selection. Kept separate from
/// <see cref="AppConfig"/> so durable preferences and throwaway session state do not mix.
/// </summary>
public sealed class AppState
{
    public const int MaxRecentNotebooks = 20;

    // Window geometry. The editor is the flexible centre pane, so only the three side panes have
    // persisted widths.
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public bool WindowMaximized { get; set; }

    public double RecentPaneWidth { get; set; } = 220;
    public double NotesPaneWidth { get; set; } = 260;
    public double AttachmentsPaneWidth { get; set; } = 260;

    // Recent notebooks, absolute paths, most recent first.
    public List<string> RecentNotebooks { get; set; } = new();

    // Current selection, restored on next launch.
    public string? CurrentNotebookPath { get; set; }
    public string? CurrentNoteId { get; set; }
}
