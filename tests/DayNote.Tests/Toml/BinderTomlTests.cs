using System;
using System.IO;
using System.Linq;
using DayNote.Core.Models;
using DayNote.Core.Storage;
using DayNote.Core.Toml;
using Xunit;

namespace DayNote.Tests.Toml;

/// <summary>
/// The writer owns the canonical on-disk shape and the reader must round-trip it losslessly, since
/// the live files are the source of truth — a serialization bug here silently corrupts user data.
/// </summary>
public sealed class BinderTomlTests
{
    [Fact]
    public void Round_trip_preserves_every_field()
    {
        var original = SampleBinder();

        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(original));

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Created, restored.Created);
        Assert.Equal(original.Modified, restored.Modified);
        Assert.Equal(original.Notes.Count, restored.Notes.Count);

        for (var i = 0; i < original.Notes.Count; i++)
        {
            var expected = original.Notes[i];
            var actual = restored.Notes[i];
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Title, actual.Title);
            Assert.Equal(expected.Created, actual.Created);
            Assert.Equal(expected.Modified, actual.Modified);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.Locked, actual.Locked);
            Assert.Equal(expected.DiscardedAt, actual.DiscardedAt);
            Assert.Equal(expected.VerifiedAt, actual.VerifiedAt);
            Assert.Equal(expected.PublishedAt, actual.PublishedAt);
            Assert.Equal(expected.RetiredAt, actual.RetiredAt);
            Assert.Equal(expected.Attachments, actual.Attachments);
            Assert.Equal(expected.Body, actual.Body);
        }
    }

    [Fact]
    public void Write_is_deterministic()
    {
        var binder = SampleBinder();
        Assert.Equal(BinderTomlWriter.Write(binder), BinderTomlWriter.Write(binder));
    }

    [Fact]
    public void Write_emits_keys_in_canonical_order()
    {
        var text = BinderTomlWriter.Write(SampleBinder());

        Assert.StartsWith("format_version = 1\nid = ", text);
        var noteStart = text.IndexOf("[[note]]", StringComparison.Ordinal);
        // The binder has no title line (its title is local app state, not stored in the file).
        Assert.DoesNotContain("title =", text[..noteStart]);
        AssertInOrder(text[..noteStart], "format_version =", "id =", "created =", "modified =");
        Assert.DoesNotContain("format_version =", text[noteStart..]);
        AssertInOrder(text[noteStart..], "id =", "title =", "created =", "modified =", "status =", "locked =", "verified_at =", "attachments =", "body =");
    }

    [Theory]
    [InlineData("id = \"nb1\"\n\n[[note]]\nid = \"n1\"\nbody = 'kept'\n")]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void A_binder_without_a_format_version_is_malformed(string text)
    {
        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(text));
    }

    [Fact]
    public void A_binder_recording_the_current_format_version_round_trips()
    {
        var text = BinderTomlWriter.Write(SampleBinder());

        Assert.Equal(FormatVersions.Binder, BinderTomlReader.FormatVersion(text));
        Assert.Equal(text, BinderTomlWriter.Write(BinderTomlReader.Read(text)));
    }

    [Fact]
    public void A_binder_recording_a_newer_format_version_is_refused_as_newer_not_malformed()
    {
        // A newer format may change any shape, and is still reported as newer rather than as corrupt.
        var text = $"format_version = {FormatVersions.Binder + 1}\nid = \"nb1\"\nnote = \"a shape this build does not know\"\n";

        var error = Assert.Throws<NewerFormatException>(() => BinderTomlReader.Read(text));

        Assert.Equal(FormatVersions.Binder + 1, error.Found);
        Assert.Equal(FormatVersions.Binder, error.Supported);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    public void A_format_version_that_is_not_a_positive_integer_is_malformed(string version)
    {
        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read($"format_version = {version}\nid = \"nb1\"\n"));
    }

    [Fact]
    public void Write_ends_with_a_trailing_newline()
    {
        Assert.EndsWith("\n", BinderTomlWriter.Write(SampleBinder()));
    }

    [Fact]
    public void Body_containing_the_literal_delimiter_round_trips_via_the_basic_string_fallback()
    {
        var binder = OneNote(body: "code block:\n''' not a real fence '''\nend");

        var text = BinderTomlWriter.Write(binder);
        // The literal-string form would be closed early by the embedded ''' , so the writer must
        // switch this one body to an escaped basic multiline string.
        Assert.Contains("body = \"\"\"", text);
        Assert.DoesNotContain("body = '''", text);

        var restored = BinderTomlReader.Read(text);
        Assert.Equal("code block:\n''' not a real fence '''\nend", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_backslashes_and_quotes_round_trips_verbatim()
    {
        var binder = OneNote(body: "path C:\\temp \"quoted\" and a tab\there");

        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));

        Assert.Equal("path C:\\temp \"quoted\" and a tab\there", restored.Notes[0].Body);
    }

    [Fact]
    public void Empty_body_round_trips_as_empty()
    {
        var binder = OneNote(body: string.Empty);

        var text = BinderTomlWriter.Write(binder);
        Assert.Contains("body = ''", text);
        Assert.Equal(string.Empty, BinderTomlReader.Read(text).Notes[0].Body);
    }

    [Fact]
    public void Title_is_normalized_to_a_single_line()
    {
        // A pasted multi-line title is flattened to one line on the way through the TOML boundary.
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(OneNote(title: "  Hello\nWorld  ")));

        Assert.Equal("Hello World", restored.Notes[0].Title);
    }

    [Fact]
    public void Non_ascii_text_round_trips()
    {
        var binder = OneNote(title: "日本語のメモ", body: "一行目\n二行目 — em dash & emoji 😀");

        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));

        Assert.Equal("日本語のメモ", restored.Notes[0].Title);
        Assert.Equal("一行目\n二行目 — em dash & emoji 😀", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_triple_double_quotes_round_trips_in_the_literal_form()
    {
        // A body with """ but no ''' stays in the literal-string ('''…''') form — """ needs no escaping
        // there — so it must not trigger the basic-string fallback, and must return byte-for-byte.
        var binder = OneNote(body: "fence:\n\"\"\" still text \"\"\"\nend");

        var text = BinderTomlWriter.Write(binder);
        Assert.Contains("body = '''", text);
        Assert.Equal("fence:\n\"\"\" still text \"\"\"\nend", BinderTomlReader.Read(text).Notes[0].Body);
    }

    [Fact]
    public void Body_with_both_delimiters_round_trips_via_the_basic_string_fallback()
    {
        // ''' forces the basic-string fallback; the embedded """ must then be escaped so it cannot close
        // the basic multiline string early. Both delimiters survive.
        var binder = OneNote(body: "a ''' and \"\"\" together");

        var text = BinderTomlWriter.Write(binder);
        Assert.Contains("body = \"\"\"", text);
        Assert.Equal("a ''' and \"\"\" together", BinderTomlReader.Read(text).Notes[0].Body);
    }

    [Fact]
    public void Body_crlf_and_lone_cr_normalize_to_lf_through_the_round_trip()
    {
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(OneNote(body: "a\r\nb\rc")));

        Assert.Equal("a\nb\nc", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_interior_blank_lines_survive_while_outer_ones_are_dropped()
    {
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(OneNote(body: "\npara one\n\npara two\n\n")));

        Assert.Equal("para one\n\npara two", restored.Notes[0].Body);
    }

    [Fact]
    public void Attachment_names_with_special_characters_round_trip()
    {
        var binder = OneNote();
        var note = binder.Notes[0];
        note.Attachments.Add("my photo (1).png");
        note.Attachments.Add("日本語 メモ.pdf");
        note.Attachments.Add("a & b.png");
        note.Attachments.Add("quote \" here.png");

        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));

        Assert.Equal(
            new[] { "my photo (1).png", "日本語 メモ.pdf", "a & b.png", "quote \" here.png" },
            restored.Notes[0].Attachments);
    }

    [Fact]
    public void Empty_title_round_trips_as_empty()
    {
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(OneNote(title: string.Empty)));

        Assert.Equal(string.Empty, restored.Notes[0].Title);
    }

    [Fact]
    public void Reader_tolerates_missing_keys_and_falls_back_for_bad_timestamps()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n" +
            "title = \"Hand edited\"\n" +
            "created = \"2026-06-03T14:23:05.482Z\"\n" +
            "\n" +
            "[[note]]\n" +
            "id = \"n1\"\n" +
            "body = '''\nhello\n'''\n";

        var binder = BinderTomlReader.Read(text);

        Assert.Equal("nb1", binder.Id);
        Assert.Equal(new DateTimeOffset(2026, 6, 3, 14, 23, 5, 482, TimeSpan.Zero), binder.Created);
        // No modified key: it takes the binder's recorded created time, never load time or
        // default(DateTimeOffset) (year 0001), which would corrupt chronological ordering.
        Assert.Equal(binder.Created, binder.Modified);
        Assert.Equal(string.Empty, binder.Notes[0].Title);
        Assert.Equal("hello", binder.Notes[0].Body);
    }

    [Fact]
    public void Reader_matches_keys_case_insensitively()
    {
        const string text = "format_version = 1\nID = \"nb1\"\n\n[[NOTE]]\nID = \"n1\"\nTITLE = \"Caps\"\nBODY = ''\n";

        var binder = BinderTomlReader.Read(text);

        Assert.Equal("nb1", binder.Id);
        Assert.Equal("n1", binder.Notes[0].Id);
        Assert.Equal("Caps", binder.Notes[0].Title);
    }

    [Fact]
    public void Reader_drops_empty_attachment_names()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nattachments = [\"a.png\", \"\", \"b.png\"]\nbody = ''\n";

        var binder = BinderTomlReader.Read(text);

        Assert.Equal(new[] { "a.png", "b.png" }, binder.Notes[0].Attachments);
    }

    [Fact]
    public void Reader_drops_attachment_names_that_are_not_bare_filenames()
    {
        // A hostile or hand-edited binder must not be able to point an attachment outside the
        // note's assets directory; only the bare filename "a.png" survives. (POSIX separators; on
        // any platform a forward slash, "." and ".." are rejected.)
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\n" +
            "attachments = [\"a.png\", \"../escape.txt\", \"sub/dir.png\", \"..\", \".\", \"/etc/passwd\"]\n" +
            "body = ''\n";

        var binder = BinderTomlReader.Read(text);

        Assert.Equal(new[] { "a.png" }, binder.Notes[0].Attachments);
    }

    [Fact]
    public void Reader_regenerates_note_ids_that_are_not_bare_names()
    {
        // A note's id becomes its attachment directory segment (<basename>-assets/<id>/), so a
        // traversal id from a hostile or hand-edited binder must never reach the storage layer. A valid
        // bare id is preserved; unsafe ids ("..", "a/b", "/x", empty) are replaced with fresh bare ids.
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n" +
            "[[note]]\nid = \"good1\"\nbody = ''\n\n" +
            "[[note]]\nid = \"..\"\nbody = ''\n\n" +
            "[[note]]\nid = \"../escape\"\nbody = ''\n\n" +
            "[[note]]\nid = \"sub/dir\"\nbody = ''\n";

        var ids = BinderTomlReader.Read(text).Notes.Select(n => n.Id).ToList();

        Assert.Equal("good1", ids[0]);
        foreach (var id in ids)
        {
            Assert.False(string.IsNullOrEmpty(id));
            Assert.Equal(id, System.IO.Path.GetFileName(id)); // a single bare segment
            Assert.NotEqual("..", id);
            Assert.NotEqual(".", id);
        }

        Assert.Equal(ids.Count, ids.Distinct().Count()); // regenerated ids stay unique within the binder
    }

    [Fact]
    public void Reader_regenerates_duplicate_note_ids_case_insensitively()
    {
        // Note ids are attachment-directory names. Duplicate ids — including names that differ only
        // in case on the fleet's default macOS/Windows filesystems — would make two notes share files
        // and would also make selection and dirty tracking ambiguous.
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n" +
            "[[note]]\nid = \"same-id\"\nbody = ''\n\n" +
            "[[note]]\nid = \"same-id\"\nbody = ''\n\n" +
            "[[note]]\nid = \"SAME-ID\"\nbody = ''\n";

        var ids = BinderTomlReader.Read(text).Notes.Select(n => n.Id).ToList();

        Assert.Equal("same-id", ids[0]);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(NoteStatus.Draft, "draft")]
    [InlineData(NoteStatus.Discarded, "discarded")]
    [InlineData(NoteStatus.Verified, "verified")]
    [InlineData(NoteStatus.Published, "published")]
    [InlineData(NoteStatus.Retired, "retired")]
    public void Status_round_trips_with_a_lowercase_token(NoteStatus status, string token)
    {
        var binder = OneNote();
        binder.Notes[0].Status = status;

        var text = BinderTomlWriter.Write(binder);
        Assert.Contains($"status = \"{token}\"", text);
        Assert.Equal(status, BinderTomlReader.Read(text).Notes[0].Status);
    }

    [Fact]
    public void Reader_defaults_missing_or_unknown_status_to_draft()
    {
        // An older file with no status key, and a hand-edit typo, both fall back to Draft.
        const string missing = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nbody = ''\n";
        const string unknown = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nstatus = \"archived\"\nbody = ''\n";

        Assert.Equal(NoteStatus.Draft, BinderTomlReader.Read(missing).Notes[0].Status);
        Assert.Equal(NoteStatus.Draft, BinderTomlReader.Read(unknown).Notes[0].Status);
    }

    [Fact]
    public void Reader_parses_status_case_insensitively()
    {
        const string text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nstatus = \"Published\"\nbody = ''\n";

        Assert.Equal(NoteStatus.Published, BinderTomlReader.Read(text).Notes[0].Status);
    }

    [Fact]
    public void Lifecycle_timestamps_round_trip()
    {
        var binder = OneNote();
        var note = binder.Notes[0];
        note.Status = NoteStatus.Published;
        note.VerifiedAt = new DateTimeOffset(2026, 6, 10, 12, 0, 0, 0, TimeSpan.Zero);
        note.PublishedAt = new DateTimeOffset(2026, 6, 10, 13, 0, 0, 0, TimeSpan.Zero);

        var text = BinderTomlWriter.Write(binder);
        Assert.Contains("verified_at =", text);
        Assert.Contains("published_at =", text);
        Assert.DoesNotContain("discarded_at =", text);
        Assert.DoesNotContain("retired_at =", text);

        var restored = BinderTomlReader.Read(text).Notes[0];
        Assert.Equal(note.VerifiedAt, restored.VerifiedAt);
        Assert.Equal(note.PublishedAt, restored.PublishedAt);
        Assert.Null(restored.DiscardedAt);
        Assert.Null(restored.RetiredAt);
    }

    [Fact]
    public void Absent_lifecycle_timestamps_read_as_null()
    {
        const string text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nstatus = \"draft\"\nbody = ''\n";

        var note = BinderTomlReader.Read(text).Notes[0];
        Assert.Null(note.DiscardedAt);
        Assert.Null(note.VerifiedAt);
        Assert.Null(note.PublishedAt);
        Assert.Null(note.RetiredAt);
    }

    [Theory]
    [InlineData(true, "locked = true")]
    [InlineData(false, "locked = false")]
    public void Locked_round_trips_as_a_boolean(bool locked, string line)
    {
        var binder = OneNote();
        binder.Notes[0].Locked = locked;

        var text = BinderTomlWriter.Write(binder);
        Assert.Contains(line, text);
        Assert.Equal(locked, BinderTomlReader.Read(text).Notes[0].Locked);
    }

    [Fact]
    public void A_missing_locked_key_reads_as_unlocked()
    {
        const string text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nstatus = \"published\"\nbody = ''\n";

        Assert.False(BinderTomlReader.Read(text).Notes[0].Locked);
    }

    [Fact]
    public void Reader_throws_a_format_exception_on_invalid_toml()
    {
        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read("this is = = not [[[ valid"));
    }

    // --- Edge cases: try to break the format ---

    // Body delimiter attacks

    [Fact]
    public void Body_that_is_exactly_triple_single_quotes()
    {
        var binder = OneNote(body: "'''");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("'''", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_ending_with_triple_single_quotes()
    {
        var binder = OneNote(body: "text ends here'''");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("text ends here'''", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_starting_with_triple_single_quotes()
    {
        var binder = OneNote(body: "'''leading delimiter");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("'''leading delimiter", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_four_consecutive_single_quotes()
    {
        var binder = OneNote(body: "a''''b");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("a''''b", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_ending_with_newline_then_triple_single_quotes()
    {
        var binder = OneNote(body: "text\n'''");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("text\n'''", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_triple_double_quotes_at_end_of_line()
    {
        var binder = OneNote(body: "line\"\"\"\n");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("line\"\"\"", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_that_is_both_delimiters_interleaved()
    {
        var binder = OneNote(body: "'''\"\"\"'''\"\"\"");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("'''\"\"\"'''\"\"\"", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_many_consecutive_single_quotes()
    {
        var quotes = new string('\'', 20);
        var binder = OneNote(body: $"before{quotes}after");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal($"before{quotes}after", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_many_consecutive_double_quotes()
    {
        var quotes = new string('"', 20);
        var binder = OneNote(body: $"before{quotes}after");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal($"before{quotes}after", restored.Notes[0].Body);
    }

    // Body whitespace / control character edge cases

    [Fact]
    public void Body_of_only_whitespace_normalizes_to_empty()
    {
        var binder = OneNote(body: "   \t  \n  \n  ");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal(string.Empty, restored.Notes[0].Body);
    }

    [Fact]
    public void Body_of_only_newlines_normalizes_to_empty()
    {
        var binder = OneNote(body: "\n\n\n\n");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal(string.Empty, restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_control_characters_stripped_on_round_trip()
    {
        var body = "before"
            + new string(new[] { '\0', (char)1, (char)2, (char)7, (char)0x0B, (char)0x0E, (char)0x0F, (char)0x7F })
            + "after";
        var binder = OneNote(body: body);
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("beforeafter", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_preserves_tabs()
    {
        var binder = OneNote(body: "\tindented\n\t\tdouble");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("\tindented\n\t\tdouble", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_mixed_line_endings_all_become_lf()
    {
        var binder = OneNote(body: "cr\ronly\r\ncrlf\nand lf");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("cr\nonly\ncrlf\nand lf", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_trailing_whitespace_on_lines_is_trimmed()
    {
        var binder = OneNote(body: "line one   \nline two\t\t\nline three");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("line one\nline two\nline three", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_backslash_at_end()
    {
        var binder = OneNote(body: "ends with backslash\\");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("ends with backslash\\", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_with_all_escape_sequences()
    {
        var binder = OneNote(body: "tab\there\nnewline\nquote\"end\\slash");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("tab\there\nnewline\nquote\"end\\slash", restored.Notes[0].Body);
    }

    [Fact]
    public void Body_single_character()
    {
        var binder = OneNote(body: "x");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("x", restored.Notes[0].Body);
    }

    // Title edge cases

    [Fact]
    public void Title_with_control_characters_survives_round_trip()
    {
        // Unlike bodies (which go through BodyCleanup), titles only get SingleLine normalization,
        // so control characters that survive TOML escaping are preserved through the round trip.
        var title = "hello" + new string(new[] { '\0', (char)1 }) + "world";
        var binder = OneNote(title: title);
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal(title, restored.Notes[0].Title);
    }

    [Fact]
    public void Title_of_only_whitespace_normalizes_to_empty()
    {
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(OneNote(title: "   \t  ")));
        Assert.Equal(string.Empty, restored.Notes[0].Title);
    }

    [Fact]
    public void Title_with_quotes_round_trips()
    {
        var binder = OneNote(title: "He said \"hello\" and 'goodbye'");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("He said \"hello\" and 'goodbye'", restored.Notes[0].Title);
    }

    [Fact]
    public void Title_with_backslashes_round_trips()
    {
        var binder = OneNote(title: "C:\\Users\\test\\file.txt");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("C:\\Users\\test\\file.txt", restored.Notes[0].Title);
    }

    [Fact]
    public void Title_with_unicode_escapes_round_trips()
    {
        var binder = OneNote(title: "emoji 😀🎉 and CJK 漢字");
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("emoji 😀🎉 and CJK 漢字", restored.Notes[0].Title);
    }

    // Structural edge cases

    [Fact]
    public void Binder_with_no_notes_round_trips()
    {
        var binder = new Binder
        {
            Id = "nb1",
            Created = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
        };
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal("nb1", restored.Id);
        Assert.Empty(restored.Notes);
    }

    [Fact]
    public void Binder_with_many_notes_round_trips()
    {
        var binder = new Binder
        {
            Id = "nb1",
            Created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        for (var i = 0; i < 100; i++)
        {
            binder.Notes.Add(new Note
            {
                Id = $"n{i:D4}",
                Title = $"Note #{i}",
                Created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Modified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Body = $"Body of note {i}",
            });
        }

        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal(100, restored.Notes.Count);
        Assert.Equal("n0099", restored.Notes[99].Id);
        Assert.Equal("Note #99", restored.Notes[99].Title);
    }

    [Fact]
    public void A_missing_binder_id_gets_a_fresh_one()
    {
        const string text = "format_version = 1\ncreated = \"2026-01-01T00:00:00.000Z\"\nmodified = \"2026-01-01T00:00:00.000Z\"\n";
        var binder = BinderTomlReader.Read(text);
        Assert.NotEqual(string.Empty, binder.Id);
    }

    [Fact]
    public void Reader_ignores_unknown_keys()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n" +
            "unknown_key = \"should be ignored\"\n" +
            "created = \"2026-01-01T00:00:00.000Z\"\n" +
            "modified = \"2026-01-01T00:00:00.000Z\"\n" +
            "\n" +
            "[[note]]\n" +
            "id = \"n1\"\n" +
            "extra_field = 42\n" +
            "body = ''\n";

        var binder = BinderTomlReader.Read(text);
        Assert.Equal("nb1", binder.Id);
        Assert.Single(binder.Notes);
    }

    [Fact]
    public void Reader_tolerates_keys_in_non_canonical_order()
    {
        const string text =
            "modified = \"2026-01-01T00:00:00.000Z\"\n" +
            "format_version = 1\nid = \"nb1\"\n" +
            "created = \"2026-01-01T00:00:00.000Z\"\n" +
            "\n" +
            "[[note]]\n" +
            "body = 'hello'\n" +
            "title = \"Reversed\"\n" +
            "status = \"verified\"\n" +
            "id = \"n1\"\n";

        var binder = BinderTomlReader.Read(text);
        Assert.Equal("nb1", binder.Id);
        Assert.Equal("Reversed", binder.Notes[0].Title);
        Assert.Equal("hello", binder.Notes[0].Body);
        Assert.Equal(NoteStatus.Verified, binder.Notes[0].Status);
    }

    // Hostile ID edge cases

    [Fact]
    public void Note_id_with_forward_slash_is_regenerated()
    {
        const string text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"a/b\"\nbody = ''\n";
        var note = BinderTomlReader.Read(text).Notes[0];
        Assert.NotEqual("a/b", note.Id);
        Assert.Equal(note.Id, Path.GetFileName(note.Id));
    }

    [Fact]
    public void Note_with_empty_id_gets_a_generated_one()
    {
        const string text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"\"\nbody = ''\n";
        var note = BinderTomlReader.Read(text).Notes[0];
        Assert.False(string.IsNullOrEmpty(note.Id));
    }

    [Fact]
    public void Note_with_missing_id_gets_a_generated_one()
    {
        const string text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nbody = ''\n";
        var note = BinderTomlReader.Read(text).Notes[0];
        Assert.False(string.IsNullOrEmpty(note.Id));
    }

    [Fact]
    public void Multiple_notes_with_hostile_ids_all_get_unique_regenerated_ids()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n" +
            "[[note]]\nid = \"..\"\nbody = ''\n\n" +
            "[[note]]\nid = \"..\"\nbody = ''\n\n" +
            "[[note]]\nid = \"\"\nbody = ''\n\n" +
            "[[note]]\nid = \".\"\nbody = ''\n";

        var ids = BinderTomlReader.Read(text).Notes.Select(n => n.Id).ToList();
        Assert.Equal(4, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id =>
        {
            Assert.False(string.IsNullOrEmpty(id));
            Assert.NotEqual(".", id);
            Assert.NotEqual("..", id);
        });
    }

    // Hostile attachment edge cases

    [Fact]
    public void Attachment_with_forward_slash_in_name_is_dropped()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\n" +
            "attachments = [\"ok.png\", \"sub/dir.png\", \"other/path/file.txt\"]\n" +
            "body = ''\n";

        var attachments = BinderTomlReader.Read(text).Notes[0].Attachments;
        Assert.Equal(new[] { "ok.png" }, attachments);
    }

    [Fact]
    public void Attachment_deeply_nested_traversal_is_dropped()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\n" +
            "attachments = [\"../../../etc/passwd\", \"a.png\"]\n" +
            "body = ''\n";

        var attachments = BinderTomlReader.Read(text).Notes[0].Attachments;
        Assert.Equal(new[] { "a.png" }, attachments);
    }

    [Fact]
    public void All_attachments_hostile_leaves_empty_list()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\n" +
            "attachments = [\"..\", \".\", \"../x\", \"a/b\", \"\", \"/root\"]\n" +
            "body = ''\n";

        Assert.Empty(BinderTomlReader.Read(text).Notes[0].Attachments);
    }

    [Fact]
    public void Missing_attachments_key_reads_as_empty_list()
    {
        const string text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nbody = ''\n";
        Assert.Empty(BinderTomlReader.Read(text).Notes[0].Attachments);
    }

    // Timestamp edge cases

    [Fact]
    public void A_note_missing_one_time_takes_its_other_recorded_time()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\ncreated = \"2026-01-01T00:00:00.000Z\"\nmodified = \"2026-01-02T00:00:00.000Z\"\n\n" +
            "[[note]]\nid = \"n1\"\nmodified = \"2026-03-01T00:00:00.000Z\"\nbody = ''\n\n" +
            "[[note]]\nid = \"n2\"\ncreated = \"2026-04-01T00:00:00.000Z\"\nmodified = \"bad\"\nbody = ''\n";

        var notes = BinderTomlReader.Read(text).Notes;

        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), notes[0].Created);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), notes[1].Modified);
    }

    [Fact]
    public void A_note_with_no_recorded_time_takes_its_binders()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\nmodified = \"2026-01-02T00:00:00.000Z\"\n\n" +
            "[[note]]\nid = \"n1\"\nbody = ''\n";

        var binder = BinderTomlReader.Read(text);

        var recorded = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(recorded, binder.Created);
        Assert.Equal(recorded, binder.Notes[0].Created);
        Assert.Equal(recorded, binder.Notes[0].Modified);
    }

    [Fact]
    public void An_empty_binder_id_gets_a_fresh_one()
    {
        const string text = "format_version = 1\nid = \"\"\n";

        Assert.NotEqual(string.Empty, BinderTomlReader.Read(text).Id);
    }

    [Fact]
    public void Malformed_binder_timestamps_fall_back_to_recent_time()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n" +
            "created = \"not-a-date\"\n" +
            "modified = \"also bad\"\n";

        var binder = BinderTomlReader.Read(text);
        Assert.True(binder.Created.Year >= 2026);
        Assert.True(binder.Modified.Year >= 2026);
    }

    [Fact]
    public void Malformed_note_timestamps_fall_back_to_recent_time()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n" +
            "[[note]]\nid = \"n1\"\n" +
            "created = \"garbage\"\nmodified = \"also garbage\"\n" +
            "body = ''\n";

        var note = BinderTomlReader.Read(text).Notes[0];
        Assert.True(note.Created.Year >= 2026);
        Assert.True(note.Modified.Year >= 2026);
    }

    [Fact]
    public void Malformed_lifecycle_timestamps_read_as_null()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n" +
            "[[note]]\nid = \"n1\"\nstatus = \"published\"\n" +
            "discarded_at = \"x\"\nverified_at = \"bad\"\npublished_at = \"nope\"\nretired_at = \"\"\n" +
            "body = ''\n";

        var note = BinderTomlReader.Read(text).Notes[0];
        Assert.Null(note.DiscardedAt);
        Assert.Null(note.VerifiedAt);
        Assert.Null(note.PublishedAt);
        Assert.Null(note.RetiredAt);
    }

    [Fact]
    public void Timestamp_with_timezone_offset_instead_of_Z_round_trips()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n" +
            "created = \"2026-06-11T09:00:00.000+09:00\"\n" +
            "modified = \"2026-06-11T00:00:00.000Z\"\n" +
            "\n[[note]]\nid = \"n1\"\nbody = ''\n";

        var binder = BinderTomlReader.Read(text);
        Assert.Equal(new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero), binder.Created);
    }

    [Fact]
    public void Timestamp_without_milliseconds_is_accepted()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n" +
            "created = \"2026-06-11T00:00:00Z\"\n" +
            "modified = \"2026-06-11T00:00:00Z\"\n" +
            "\n[[note]]\nid = \"n1\"\nbody = ''\n";

        var binder = BinderTomlReader.Read(text);
        Assert.Equal(2026, binder.Created.Year);
    }

    [Fact]
    public void All_lifecycle_timestamps_set_round_trip()
    {
        var binder = OneNote();
        var note = binder.Notes[0];
        note.Status = NoteStatus.Retired;
        note.DiscardedAt = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero);
        note.VerifiedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        note.PublishedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        note.RetiredAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder)).Notes[0];
        Assert.Equal(note.DiscardedAt, restored.DiscardedAt);
        Assert.Equal(note.VerifiedAt, restored.VerifiedAt);
        Assert.Equal(note.PublishedAt, restored.PublishedAt);
        Assert.Equal(note.RetiredAt, restored.RetiredAt);
    }

    // Large / stress inputs

    [Fact]
    public void Large_body_round_trips()
    {
        var body = string.Join("\n", Enumerable.Range(0, 1000).Select(i => $"Line {i}: {new string('x', 100)}"));
        var binder = OneNote(body: body);
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal(body, restored.Notes[0].Body);
    }

    [Fact]
    public void Long_single_line_body_round_trips()
    {
        var body = new string('a', 100_000);
        var binder = OneNote(body: body);
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal(body, restored.Notes[0].Body);
    }

    [Fact]
    public void Long_title_round_trips()
    {
        var title = new string('Z', 10_000);
        var binder = OneNote(title: title);
        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal(title, restored.Notes[0].Title);
    }

    [Fact]
    public void Many_attachments_round_trip()
    {
        var binder = OneNote();
        for (var i = 0; i < 200; i++)
            binder.Notes[0].Attachments.Add($"file_{i:D4}.png");

        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder));
        Assert.Equal(200, restored.Notes[0].Attachments.Count);
        Assert.Equal("file_0199.png", restored.Notes[0].Attachments[199]);
    }

    // Idempotency: write → read → write produces identical output

    [Fact]
    public void Write_read_write_is_idempotent()
    {
        var binder = SampleBinder();
        var first = BinderTomlWriter.Write(binder);
        var second = BinderTomlWriter.Write(BinderTomlReader.Read(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Write_read_write_is_idempotent_with_complex_body()
    {
        var binder = OneNote(body: "tab\there\nquote\"s\nslash\\\nempty:\n\nend");
        var first = BinderTomlWriter.Write(binder);
        var second = BinderTomlWriter.Write(BinderTomlReader.Read(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Write_read_write_is_idempotent_with_literal_fallback_body()
    {
        var binder = OneNote(body: "a ''' triggers the fallback path");
        var first = BinderTomlWriter.Write(binder);
        var second = BinderTomlWriter.Write(BinderTomlReader.Read(first));
        Assert.Equal(first, second);
    }

    // Mixed / realistic scenarios

    [Fact]
    public void Note_with_every_field_populated_round_trips()
    {
        var binder = new Binder
        {
            Id = "binder1",
            Created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero),
        };
        var note = new Note
        {
            Id = "fullNote",
            Title = "Every field populated: \"quotes\" and 'apostrophes' \\ backslash",
            Created = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero),
            Status = NoteStatus.Retired,
            Locked = true,
            VerifiedAt = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero),
            PublishedAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            RetiredAt = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            Body = "Line 1\n\tindented\n\n\"\"\" triple doubles\n''' triple singles\n\\backslash at end\\",
        };
        note.Attachments.Add("photo (1).jpg");
        note.Attachments.Add("日本語.pdf");
        binder.Notes.Add(note);

        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder)).Notes[0];
        Assert.Equal(note.Id, restored.Id);
        Assert.Equal(note.Title, restored.Title);
        Assert.Equal(note.Created, restored.Created);
        Assert.Equal(note.Modified, restored.Modified);
        Assert.Equal(note.Status, restored.Status);
        Assert.Equal(note.Locked, restored.Locked);
        Assert.Equal(note.VerifiedAt, restored.VerifiedAt);
        Assert.Equal(note.PublishedAt, restored.PublishedAt);
        Assert.Equal(note.RetiredAt, restored.RetiredAt);
        Assert.Equal(note.Body, restored.Body);
        Assert.Equal(note.Attachments, restored.Attachments);
    }

    [Fact]
    public void Mix_of_valid_and_hostile_notes_reads_correctly()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n" +
            "created = \"2026-01-01T00:00:00.000Z\"\n" +
            "modified = \"2026-01-01T00:00:00.000Z\"\n" +
            "\n" +
            "[[note]]\n" +
            "id = \"good\"\ntitle = \"Valid\"\nbody = 'hello'\n" +
            "\n" +
            "[[note]]\n" +
            "id = \"../bad\"\ntitle = \"Hostile id\"\n" +
            "attachments = [\"../escape\", \"ok.png\"]\n" +
            "body = 'world'\n";

        var binder = BinderTomlReader.Read(text);
        Assert.Equal(2, binder.Notes.Count);
        Assert.Equal("good", binder.Notes[0].Id);
        Assert.Equal("Valid", binder.Notes[0].Title);
        Assert.NotEqual("../bad", binder.Notes[1].Id);
        Assert.Equal("Hostile id", binder.Notes[1].Title);
        Assert.Equal(new[] { "ok.png" }, binder.Notes[1].Attachments);
    }

    [Fact]
    public void Hand_edited_file_with_inline_tables_throws_or_reads_gracefully()
    {
        // Someone might try to hand-edit and use an inline note instead of [[note]].
        // This should either throw or produce zero notes (no notes matched [[note]]).
        const string text = "format_version = 1\nid = \"nb1\"\nnote = [{id = \"n1\", body = \"hi\"}]\n";

        // Tomlyn may parse this as a different structure; we just need it not to crash.
        var binder = BinderTomlReader.Read(text);
        Assert.Equal("nb1", binder.Id);
    }

    private static void AssertInOrder(string text, params string[] tokens)
    {
        var last = -1;
        foreach (var token in tokens)
        {
            var index = text.IndexOf(token, StringComparison.Ordinal);
            Assert.True(index > last, $"Expected '{token}' to appear after position {last}, but found it at {index}.");
            last = index;
        }
    }

    private static Binder SampleBinder()
    {
        var binder = new Binder
        {
            Id = "V1StGXR8Z5jdHi6Bmy3kT",
            Created = new DateTimeOffset(2026, 6, 3, 14, 23, 5, 482, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 3, 15, 1, 22, 1, TimeSpan.Zero),
        };

        var first = new Note
        {
            Id = "a7F0kQ2mN8pL3vX9wZ1cR",
            Title = "First note",
            Created = new DateTimeOffset(2026, 6, 3, 14, 30, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 3, 14, 30, 0, 0, TimeSpan.Zero),
            Status = NoteStatus.Verified,
            VerifiedAt = new DateTimeOffset(2026, 6, 3, 15, 0, 0, 0, TimeSpan.Zero),
            Body = "firstline\n\tsecondline\nthirdline",
        };
        first.Attachments.Add("diagram.png");
        first.Attachments.Add("notes.pdf");
        binder.Notes.Add(first);

        var second = new Note
        {
            Id = "Yt4Bn6Hs0Dx2Gq8Lm5Pw1",
            Title = "Second note",
            Created = new DateTimeOffset(2026, 6, 3, 14, 40, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 3, 14, 41, 0, 0, TimeSpan.Zero),
            Status = NoteStatus.Published,
            Locked = true,
            VerifiedAt = new DateTimeOffset(2026, 6, 3, 14, 45, 0, 0, TimeSpan.Zero),
            PublishedAt = new DateTimeOffset(2026, 6, 3, 14, 50, 0, 0, TimeSpan.Zero),
            Body = "copies clean, indentation preserved exactly",
        };
        binder.Notes.Add(second);

        return binder;
    }

    private static Binder OneNote(string title = "Note", string body = "")
    {
        var binder = new Binder
        {
            Id = "nb1",
            Created = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
        };
        binder.Notes.Add(new Note
        {
            Id = "n1",
            Title = title,
            Created = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            Body = body,
        });
        return binder;
    }
}
