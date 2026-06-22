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
    public DateTimeOffset Modified { get; set; }

    /// <summary>Lifecycle state (draft / ready / published / expired). New notes start as draft.</summary>
    public NoteStatus Status { get; set; } = NoteStatus.Draft;

    /// <summary>Set when the note first reaches ready; cleared only on return to draft.</summary>
    public DateTimeOffset? ReadyAt { get; set; }

    /// <summary>Set on first publish; cleared only on return to draft.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Set when the note first reaches expired; cleared only on return to draft.</summary>
    public DateTimeOffset? ExpiredAt { get; set; }

    /// <summary>Bare attachment filenames, in the order written to the file.</summary>
    public List<string> Attachments { get; } = new();

    /// <summary>Plain-text body. Stored verbatim; normalized by <c>BodyCleanup</c> on read and write.</summary>
    public string Body { get; set; } = string.Empty;
}
