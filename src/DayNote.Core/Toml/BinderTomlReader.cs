using DayNote.Core.Identity;
using DayNote.Core.Models;
using DayNote.Core.Text;
using DayNote.Core.Time;
using Tomlyn;

namespace DayNote.Core.Toml;

/// <summary>
/// Parses <c>.daynote</c> TOML text into a <see cref="Binder"/>. Field order is irrelevant on
/// read; only the canonical writer enforces order. Reading is case-insensitive and tolerant of
/// missing keys so hand-edited files still load. Bodies are run through <see cref="BodyCleanup"/>
/// so the in-memory body equals the canonical stored form (this also removes the trailing newline
/// that TOML multiline strings retain).
/// </summary>
public static class BinderTomlReader
{
    private static readonly TomlSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static Binder Read(string text)
    {
        BinderDocument? document;
        try
        {
            document = TomlSerializer.Deserialize<BinderDocument>(text, Options);
        }
        catch (TomlException ex)
        {
            throw new BinderFormatException($"Binder is not valid TOML: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new BinderFormatException("Binder is empty or not a TOML table.");
        }

        // Timestamps that are absent or malformed (a hand-edit typo) fall back to load time rather
        // than to default(DateTimeOffset), which would otherwise be written back as a bogus
        // year-0001 date and corrupt chronological ordering.
        var fallback = DateTimeOffset.UtcNow;

        var binder = new Binder
        {
            Id = document.Id ?? string.Empty,
            Created = ParseTimestamp(document.Created, fallback),
            Modified = ParseTimestamp(document.Modified, fallback),
        };

        if (document.Note is { } notes)
        {
            // Track ids already assigned in this binder so a regenerated (unsafe) id can be made unique.
            var noteIds = new List<string>(notes.Count);
            foreach (var noteDocument in notes)
            {
                var note = MapNote(noteDocument, fallback, noteIds);
                noteIds.Add(note.Id);
                binder.Notes.Add(note);
            }
        }

        return binder;
    }

    private static Note MapNote(NoteDocument document, DateTimeOffset fallback, IReadOnlyCollection<string> existingIds)
    {
        var note = new Note
        {
            Id = SafeNoteId(document.Id, existingIds),
            Title = TextCleanup.SingleLine(document.Title ?? string.Empty),
            Created = ParseTimestamp(document.Created, fallback),
            Modified = ParseTimestamp(document.Modified, fallback),
            Status = NoteStatuses.Parse(document.Status),
            Body = BodyCleanup.Normalize(document.Body ?? string.Empty),
        };

        if (document.Attachments is { } attachments)
        {
            foreach (var name in attachments)
            {
                if (IsBareFileName(name))
                {
                    note.Attachments.Add(name);
                }
            }
        }

        return note;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a single bare filename: non-empty, with no directory
    /// component and not a <c>.</c>/<c>..</c> traversal segment. Attachment references must be bare
    /// filenames resolved under the note's assets directory (see <see cref="Models.Note"/>); a name
    /// carrying a path separator or a traversal segment — which the app never writes, but a
    /// hand-edited or hostile binder could — would resolve <em>outside</em> that directory, where
    /// removing it would delete an unrelated file. Such names are dropped on read, the same way empty
    /// names are, so the malformed reference never reaches the storage layer.
    /// </summary>
    private static bool IsBareFileName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name == Path.GetFileName(name)
        && name != "."
        && name != "..";

    /// <summary>
    /// Returns a note id safe to use as a directory segment. A note's id becomes its attachment
    /// directory name (<c>&lt;basename&gt;-assets/&lt;note-id&gt;/</c>), so an id carrying a path
    /// separator or a <c>.</c>/<c>..</c> traversal segment — which the app never writes, but a
    /// hand-edited or hostile binder could — would resolve attachment writes/deletes <em>outside</em>
    /// that directory. The app only ever assigns generated bare ids, so any non-bare (or empty) id is
    /// malformed and is replaced with a fresh id unique within the binder rather than reaching the
    /// storage layer. This mirrors the attachment-name guard in <see cref="IsBareFileName"/>.
    /// </summary>
    private static string SafeNoteId(string? id, IReadOnlyCollection<string> existingIds) =>
        IsBareFileName(id) ? id! : IdGenerator.NewUnique(existingIds);

    private static DateTimeOffset ParseTimestamp(string? text, DateTimeOffset fallback) =>
        !string.IsNullOrWhiteSpace(text) && DayNoteTime.TryParseIso(text, out var value) ? value : fallback;

    // Internal DTOs mirroring the on-disk shape. Timestamps are read as strings because the format
    // stores them as quoted ISO-8601 values rather than TOML-native datetimes.
    private sealed class BinderDocument
    {
        public string? Id { get; set; }
        public string? Created { get; set; }
        public string? Modified { get; set; }
        public List<NoteDocument>? Note { get; set; }
    }

    private sealed class NoteDocument
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Created { get; set; }
        public string? Modified { get; set; }
        public string? Status { get; set; }
        public List<string>? Attachments { get; set; }
        public string? Body { get; set; }
    }
}
