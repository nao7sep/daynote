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
    /// Parses a stored token back to a status, case-insensitively. An unrecognized token is not a status
    /// and parses to nothing; it is never taken for another status.
    /// </summary>
    public static bool TryParse(string token, out NoteStatus status)
    {
        NoteStatus? parsed = token.Trim().ToLowerInvariant() switch
        {
            "draft" => NoteStatus.Draft,
            "discarded" => NoteStatus.Discarded,
            "verified" => NoteStatus.Verified,
            "published" => NoteStatus.Published,
            "retired" => NoteStatus.Retired,
            _ => null,
        };
        status = parsed ?? NoteStatus.Draft;
        return parsed is not null;
    }
}
