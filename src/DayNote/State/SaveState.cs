namespace DayNote.State;

/// <summary>The save status of the current binder, surfaced in the status bar. Ported from quickdeck.</summary>
public enum SaveState
{
    Saved,
    Saving,
    Unsaved,
    Error,
}
