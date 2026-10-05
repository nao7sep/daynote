namespace DayNote.Core.Models;

/// <summary>
/// A single text entry within a binder. Attachments are referenced by bare filename, matching
/// the on-disk file format; the files themselves live in
/// <c>&lt;binder-basename&gt;-assets/&lt;note-id&gt;/</c> beside the binder.
/// </summary>
public sealed class Note
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Created { get; set; }

    /// <summary>When the note's content (title, body, attachments) last changed, per the content-lifecycle-conventions.</summary>
    public DateTimeOffset Modified { get; set; }

    /// <summary>Lifecycle state. New notes start as draft; <see cref="NoteLifecycle"/> owns the moves.</summary>
    public NoteStatus Status { get; set; } = NoteStatus.Draft;

    /// <summary>Whether the content is locked against edits. The status and deletion stay free.</summary>
    public bool Locked { get; set; }

    public DateTimeOffset? DiscardedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>Bare attachment filenames, in the order written to the file.</summary>
    public List<string> Attachments { get; } = new();

    /// <summary>Plain-text body. Stored verbatim; normalized by <c>BodyCleanup</c> on read and write.</summary>
    public string Body { get; set; } = string.Empty;
}
