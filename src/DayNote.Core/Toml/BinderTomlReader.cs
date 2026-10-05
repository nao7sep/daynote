using DayNote.Core.Identity;
using DayNote.Core.Models;
using DayNote.Core.Storage;
using DayNote.Core.Text;
using DayNote.Core.Time;
using Tomlyn;
using Tomlyn.Serialization;

namespace DayNote.Core.Toml;

/// <summary>
/// Parses <c>.daynote</c> TOML text into a <see cref="Binder"/>. Field order is irrelevant on
/// read; only the canonical writer enforces order. Reading is case-insensitive and tolerant of
/// missing keys so hand-edited files still load. Bodies are run through <see cref="BodyCleanup"/>
/// so the in-memory body equals the canonical stored form (this also removes the trailing newline
/// that TOML multiline strings retain). The format version is read before anything else, so a binder
/// written by a newer DayNote is reported as newer, never as malformed, whatever its shape.
/// </summary>
public static class BinderTomlReader
{
    private static readonly TomlSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <exception cref="NewerFormatException">The binder records a newer format than this build reads.</exception>
    /// <exception cref="BinderFormatException">The text is not a binder.</exception>
    public static Binder Read(string text)
    {
        var version = FormatVersion(text);
        if (version > FormatVersions.Binder)
        {
            throw new NewerFormatException("binder", version, FormatVersions.Binder);
        }

        var document = Deserialize<BinderDocument>(text);

        // A timestamp that is absent or malformed (a hand-edit typo) takes another time the same item
        // recorded, then its binder's, and the load time only when the file records none at all, per
        // the content-lifecycle-conventions. Never default(DateTimeOffset): it would be written back
        // as a bogus year-0001 date and corrupt chronological ordering.
        var binderCreated = ParseOptionalTimestamp(document.Created);
        var binderModified = ParseOptionalTimestamp(document.Modified);
        var fallback = binderCreated ?? binderModified ?? DateTimeOffset.UtcNow;

        var binder = new Binder
        {
            Id = string.IsNullOrEmpty(document.Id) ? IdGenerator.New() : document.Id,
            Created = fallback,
            Modified = binderModified ?? fallback,
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

    /// <summary>The format version the binder text records: its <c>format_version</c>, or 1 when absent.</summary>
    /// <exception cref="BinderFormatException">The text is not a TOML table, or the version is not a positive integer.</exception>
    public static long FormatVersion(string text) =>
        Deserialize<VersionDocument>(text).FormatVersion switch
        {
            null => 1,
            >= 1 and var version => version,
            _ => throw new BinderFormatException("Binder format_version is not a positive integer."),
        };

    private static T Deserialize<T>(string text)
        where T : class
    {
        T? document;
        try
        {
            document = TomlSerializer.Deserialize<T>(text, Options);
        }
        catch (TomlException ex)
        {
            throw new BinderFormatException($"Binder is not valid TOML: {ex.Message}", ex);
        }

        return document ?? throw new BinderFormatException("Binder is empty or not a TOML table.");
    }

    private static Note MapNote(NoteDocument document, DateTimeOffset fallback, IReadOnlyCollection<string> existingIds)
    {
        var created = ParseOptionalTimestamp(document.Created);
        var modified = ParseOptionalTimestamp(document.Modified);
        var note = new Note
        {
            Id = SafeNoteId(document.Id, existingIds),
            Title = TextCleanup.SingleLine(document.Title ?? string.Empty),
            Created = created ?? modified ?? fallback,
            Modified = modified ?? created ?? fallback,
            Status = NoteStatuses.Parse(document.Status),
            Locked = document.Locked ?? false,
            DiscardedAt = ParseOptionalTimestamp(document.DiscardedAt),
            VerifiedAt = ParseOptionalTimestamp(document.VerifiedAt),
            PublishedAt = ParseOptionalTimestamp(document.PublishedAt),
            RetiredAt = ParseOptionalTimestamp(document.RetiredAt),
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
    /// that directory. The app only ever assigns unique generated bare ids, so any non-bare, empty,
    /// duplicate, or case-only-colliding id is malformed and is replaced with a fresh id unique within
    /// the binder rather than reaching the storage layer. Case-insensitive uniqueness matches the
    /// default macOS and Windows filesystems. This mirrors the attachment-name guard in
    /// <see cref="IsBareFileName"/>.
    /// </summary>
    private static string SafeNoteId(string? id, IReadOnlyCollection<string> existingIds) =>
        IsBareFileName(id) && !existingIds.Contains(id!, StringComparer.OrdinalIgnoreCase)
            ? id!
            : IdGenerator.NewUnique(existingIds);

    private static DateTimeOffset? ParseOptionalTimestamp(string? text) =>
        !string.IsNullOrWhiteSpace(text) && DayNoteTime.TryParseIso(text, out var value) ? value : null;

    // Internal DTOs mirroring the on-disk shape. Timestamps are read as strings because the format
    // stores them as quoted ISO-8601 values rather than TOML-native datetimes.
    private sealed class VersionDocument
    {
        [TomlPropertyName("format_version")]
        public long? FormatVersion { get; set; }
    }

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
        public bool? Locked { get; set; }
        [TomlPropertyName("discarded_at")]
        public string? DiscardedAt { get; set; }
        [TomlPropertyName("verified_at")]
        public string? VerifiedAt { get; set; }
        [TomlPropertyName("published_at")]
        public string? PublishedAt { get; set; }
        [TomlPropertyName("retired_at")]
        public string? RetiredAt { get; set; }
        public List<string>? Attachments { get; set; }
        public string? Body { get; set; }
    }
}
