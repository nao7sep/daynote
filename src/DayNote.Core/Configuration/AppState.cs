namespace DayNote.Core.Configuration;

/// <summary>
/// Volatile session state, persisted to <c>~/.daynote/state.json</c>: pane widths and the current
/// selection. Kept separate from <see cref="AppConfig"/> so durable preferences
/// and throwaway session state do not mix. Main-window geometry is disposable session state too.
/// </summary>
public sealed class AppState
{
    // The pixel width the user last dragged each side pane to (the "intent"). The editor pane is the
    // fill column (star-sized) and is not persisted — it absorbs whatever space remains. On restore
    // each intent is clamped to [MinWidth, whatFitsTheCurrentWindow] so a stale value can never
    // reopen a pane below its minimum or push the editor below its own minimum.
    public double BindersPaneWidth { get; set; } = 220;
    public double NotesPaneWidth { get; set; } = 260;
    public double AttachmentsPaneWidth { get; set; } = 260;

    // Last normal main-window geometry. Nullable primitives distinguish an absent placement from
    // coordinates at the origin; negative positions are valid on screens left of or above primary.
    public int? WindowPositionX { get; set; }
    public int? WindowPositionY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    // Current selection, restored on next launch.
    public string? CurrentBinderPath { get; set; }
    public string? CurrentNoteId { get; set; }
}
