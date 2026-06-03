namespace DayNote.Core.Models;

/// <summary>
/// One <c>.daynote</c> file: an ordered collection of notes plus notebook-level metadata.
/// The order of <see cref="Notes"/> is the canonical document order and is never reordered on
/// write — new notes are appended, deletions are removed in place. Timestamp-based ordering is a
/// presentation concern handled elsewhere.
/// </summary>
public sealed class Notebook
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }

    /// <summary>Notes in canonical document order.</summary>
    public List<Note> Notes { get; } = new();
}
