namespace DayNote.Core.Models;

/// <summary>
/// One <c>.daynote</c> file: an ordered collection of notes plus binder-level metadata. The binder's
/// display <em>title</em> is deliberately not stored here — a binder is a collection, and its title is
/// a local label kept in app state, so it does not travel with the file (see <c>KnownBinder</c>).
/// The order of <see cref="Notes"/> is the canonical document order and is never reordered on
/// write — new notes are appended, deletions are removed in place. Timestamp-based ordering is a
/// presentation concern handled elsewhere.
/// </summary>
public sealed class Binder
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }

    /// <summary>Notes in canonical document order.</summary>
    public List<Note> Notes { get; } = new();
}
