namespace DayNote.Core.Models;

/// <summary>
/// A note's lifecycle state. Ordered draft → checked → published as a natural workflow, with
/// <see cref="Expired"/> as a terminal state for entries that have aged out. New notes start as
/// <see cref="Draft"/>. The state is a manual choice the user makes; nothing transitions it
/// automatically (a future time-based expiry could, but is not built yet).
/// </summary>
public enum NoteStatus
{
    Draft,
    Checked,
    Published,
    Expired,
}

/// <summary>Serialization for <see cref="NoteStatus"/>: stable lowercase tokens in the stored file.</summary>
public static class NoteStatuses
{
    /// <summary>The lowercase token written to the <c>.daynote</c> file (matches bigmouth's vocabulary).</summary>
    public static string ToToken(this NoteStatus status) => status switch
    {
        NoteStatus.Checked => "checked",
        NoteStatus.Published => "published",
        NoteStatus.Expired => "expired",
        _ => "draft",
    };

    /// <summary>
    /// Parses a stored token back to a status, case-insensitively. Anything missing or unrecognized
    /// (a hand-edit typo, or an older file with no status) falls back to <see cref="NoteStatus.Draft"/>.
    /// </summary>
    public static NoteStatus Parse(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "checked" => NoteStatus.Checked,
        "published" => NoteStatus.Published,
        "expired" => NoteStatus.Expired,
        _ => NoteStatus.Draft,
    };
}
