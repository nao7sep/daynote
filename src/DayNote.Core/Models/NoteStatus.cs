namespace DayNote.Core.Models;

/// <summary>
/// A note's lifecycle state. Two editable (<see cref="Draft"/>, <see cref="Ready"/>) and two
/// locked (<see cref="Published"/>, <see cref="Expired"/>). New notes start as <see cref="Draft"/>.
/// Transitions are manual and any-to-any; nothing auto-transitions.
/// </summary>
public enum NoteStatus
{
    Draft,
    Ready,
    Published,
    Expired,
}

/// <summary>Serialization for <see cref="NoteStatus"/>: stable lowercase tokens in the stored file.</summary>
public static class NoteStatuses
{
    /// <summary>The lowercase token written to the <c>.daynote</c> file (matches bigmouth's vocabulary).</summary>
    public static string ToToken(this NoteStatus status) => status switch
    {
        NoteStatus.Ready => "ready",
        NoteStatus.Published => "published",
        NoteStatus.Expired => "expired",
        _ => "draft",
    };

    /// <summary>
    /// Parses a stored token back to a status, case-insensitively. Accepts the legacy token
    /// <c>"checked"</c> as <see cref="NoteStatus.Ready"/> for backward compatibility with older files.
    /// Anything missing or unrecognized falls back to <see cref="NoteStatus.Draft"/>.
    /// </summary>
    public static NoteStatus Parse(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "ready" => NoteStatus.Ready,
        "checked" => NoteStatus.Ready,
        "published" => NoteStatus.Published,
        "expired" => NoteStatus.Expired,
        _ => NoteStatus.Draft,
    };

    /// <summary>Whether the status is in the editable half of the lifecycle (draft or ready).</summary>
    public static bool IsEditable(this NoteStatus status) =>
        status is NoteStatus.Draft or NoteStatus.Ready;
}
