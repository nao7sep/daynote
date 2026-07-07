namespace DayNote.Core.Models;

/// <summary>
/// Applies the content-lifecycle timestamp rules when a note's status changes.
/// Timestamps are set-if-absent on forward moves and cleared only on return to draft.
/// </summary>
public static class NoteLifecycle
{
    public static void ApplyTransition(Note note, NoteStatus newStatus, DateTimeOffset now)
    {
        if (newStatus == NoteStatus.Draft)
        {
            note.ReadyAt = null;
            note.PublishedAt = null;
            note.ExpiredAt = null;
        }
        else if (newStatus == NoteStatus.Ready)
        {
            note.ReadyAt ??= now;
        }
        else if (newStatus == NoteStatus.Published)
        {
            note.ReadyAt ??= now;
            note.PublishedAt ??= now;
        }
        else if (newStatus == NoteStatus.Expired)
        {
            note.ReadyAt ??= now;
            note.PublishedAt ??= now;
            note.ExpiredAt ??= now;
        }

        note.Status = newStatus;
    }
}
