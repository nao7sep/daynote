namespace DayNote.Core.Models;

/// <summary>
/// Moves a note to a new status and sets its status times by the transition table of the
/// content-lifecycle-conventions. Retired implies published and published implies verified, so an
/// implied time is set with the move when the source status cannot hold it, and kept when it can.
/// </summary>
public static class NoteLifecycle
{
    public static void ApplyTransition(Note note, NoteStatus target, DateTimeOffset now)
    {
        var source = note.Status;
        if (source == target)
        {
            return;
        }

        // Which times the source status holds, so each is kept rather than re-set.
        var holdsVerified = source is NoteStatus.Verified or NoteStatus.Published or NoteStatus.Retired;
        var holdsPublished = source is NoteStatus.Published or NoteStatus.Retired;

        DateTimeOffset? discarded = null, verified = null, published = null, retired = null;
        switch (target)
        {
            case NoteStatus.Discarded:
                discarded = now;
                break;
            case NoteStatus.Verified:
                verified = Keep(holdsVerified, note.VerifiedAt, now);
                break;
            case NoteStatus.Published:
                verified = Keep(holdsVerified, note.VerifiedAt, now);
                // Retired → published is an undelete: the original publication time comes back with it.
                published = source == NoteStatus.Retired
                    ? Keep(holdsPublished, note.PublishedAt, NotBefore(now, verified))
                    : NotBefore(now, verified);
                break;
            case NoteStatus.Retired:
                verified = Keep(holdsVerified, note.VerifiedAt, now);
                published = Keep(holdsPublished, note.PublishedAt, NotBefore(now, verified));
                retired = NotBefore(now, published);
                break;
        }

        note.DiscardedAt = discarded;
        note.VerifiedAt = verified;
        note.PublishedAt = published;
        note.RetiredAt = retired;
        note.Status = target;
    }

    private static DateTimeOffset Keep(bool holds, DateTimeOffset? existing, DateTimeOffset now) =>
        holds && existing is { } value ? value : now;

    // A new time never precedes a kept time it follows, even if the clock stepped back.
    private static DateTimeOffset NotBefore(DateTimeOffset now, DateTimeOffset? earlier) =>
        earlier is { } value && value > now ? value : now;
}
