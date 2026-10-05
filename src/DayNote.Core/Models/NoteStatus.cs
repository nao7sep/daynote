namespace DayNote.Core.Models;

/// <summary>
/// A note's lifecycle state, declared in workflow order (the status picker follows it). New notes
/// start as <see cref="Draft"/>. Transitions are manual and any-to-any; nothing auto-transitions.
/// Whether a note can be edited is its <see cref="Note.Locked"/> flag, never its status.
/// </summary>
public enum NoteStatus
{
    Draft,
    Discarded,
    Verified,
    Published,
    Retired,
}

/// <summary>Serialization for <see cref="NoteStatus"/>: stable lowercase tokens in the stored file.</summary>
public static class NoteStatuses
{
    /// <summary>The lowercase token written to the <c>.daynote</c> file.</summary>
    public static string ToToken(this NoteStatus status) => status switch
    {
        NoteStatus.Discarded => "discarded",
        NoteStatus.Verified => "verified",
        NoteStatus.Published => "published",
        NoteStatus.Retired => "retired",
        _ => "draft",
    };

    /// <summary>
    /// Parses a stored token back to a status, case-insensitively. Anything missing or unrecognized
    /// falls back to <see cref="NoteStatus.Draft"/>.
    /// </summary>
    public static NoteStatus Parse(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "discarded" => NoteStatus.Discarded,
        "verified" => NoteStatus.Verified,
        "published" => NoteStatus.Published,
        "retired" => NoteStatus.Retired,
        _ => NoteStatus.Draft,
    };
}
