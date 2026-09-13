namespace DayNote.Core.Configuration;

/// <summary>
/// Volatile session state, persisted to <c>~/.daynote/state.json</c>: pane widths, the known-binders
/// list, and the current selection. Kept separate from <see cref="AppConfig"/> so durable preferences
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

    // Known binders, each a file path plus its locally-stored display title. Not capped — the user
    // prunes the list explicitly via the row ✕, so a known binder never silently disappears. The title
    // lives here (per machine), not in the .daynote file: a binder is a collection, and a collection's
    // title is a local label, so it is intentionally not carried with the file to other computers.
    // (Renamed from the earlier string-only RecentBinders; the old key is simply ignored on load.)
    public List<KnownBinder> Binders { get; set; } = new();

    // Current selection, restored on next launch.
    public string? CurrentBinderPath { get; set; }
    public string? CurrentNoteId { get; set; }

}

/// <summary>A binder the user has opened: its file path and the locally-stored display title.</summary>
public sealed class KnownBinder
{
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
