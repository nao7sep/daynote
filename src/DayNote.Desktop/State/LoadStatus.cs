namespace DayNote.Desktop.State;

/// <summary>
/// Tracks whether persisted data has been read off disk yet. Every write path gates on
/// <see cref="Ready"/> so a failed load can never overwrite existing files with default state.
/// Ported from quickdeck.
/// </summary>
public enum LoadStatus
{
    Loading,
    Ready,
    Failed,
}
