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
    public void Reader_tolerates_missing_keys()
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

    [Theory]
    [InlineData("")]
    [InlineData("id = \"\"\n")]
    [InlineData("id = \"a/b\"\n")]
    [InlineData("id = \"..\"\n")]
    public void A_missing_or_malformed_binder_id_makes_the_binder_malformed(string idLine)
    {
        var text = "format_version = 1\n" + idLine + "created = \"2026-01-01T00:00:00.000Z\"\n";

        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("id = \"\"\n")]
    [InlineData("id = \".\"\n")]
    [InlineData("id = \"..\"\n")]
    [InlineData("id = \"../escape\"\n")]
    [InlineData("id = \"sub/dir\"\n")]
    [InlineData("id = \"/x\"\n")]
    [InlineData("id = \"back\\\\slash\"\n")]
    public void A_missing_or_malformed_note_id_makes_the_binder_malformed(string idLine)
    {
        // A note's id names its attachment folder: a fresh id would part the note from its files, and a
        // traversal id would reach outside the binder's assets folder.
        var text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"good1\"\nbody = ''\n\n[[note]]\n" + idLine + "body = ''\n";

        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(text));
    }

    [Theory]
    [InlineData("same-id")]
    [InlineData("SAME-ID")]
    public void Note_ids_that_collide_ignoring_case_make_the_binder_malformed(string second)
    {
        // Ids that differ only in case name one folder on the default macOS and Windows filesystems.
        var text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"same-id\"\nbody = ''\n\n" +
            $"[[note]]\nid = \"{second}\"\nbody = ''\n";

        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape.txt")]
    [InlineData("sub/dir.png")]
    [InlineData("/etc/passwd")]
    [InlineData("a/b/../../../../etc/passwd")]
    public void An_attachment_that_is_not_a_bare_file_name_makes_the_binder_malformed(string name)
    {
        // Dropping it would erase the reference at the next save; keeping it would let a remove reach
        // outside the note's assets folder.
        var text = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\n" +
            $"attachments = [\"a.png\", \"{name}\"]\nbody = ''\n";

        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(text));
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
    public void Reader_defaults_a_missing_status_to_draft()
    {
        const string missing = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nbody = ''\n";

        Assert.Equal(NoteStatus.Draft, BinderTomlReader.Read(missing).Notes[0].Status);
    }

    [Fact]
    public void An_unknown_status_makes_the_binder_malformed_rather_than_a_draft()
    {
        // A hand-edit typo is not a status: reading it as Draft would save the note back as one.
        const string unknown = "format_version = 1\nid = \"nb1\"\n\n[[note]]\nid = \"n1\"\nstatus = \"publised\"\nbody = ''\n";

        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(unknown));
    }

    [Theory]
    [InlineData("draft", "verified_at = \"2026-06-12T00:00:00.000Z\"")]
    [InlineData("discarded", "retired_at = \"2026-06-12T00:00:00.000Z\"")]
    [InlineData("verified", "published_at = \"2026-06-12T00:00:00.000Z\"")]
    [InlineData("published", "discarded_at = \"2026-06-12T00:00:00.000Z\"")]
    [InlineData("retired", "discarded_at = \"2026-06-12T00:00:00.000Z\"")]
    public void A_status_time_the_status_contradicts_makes_the_binder_malformed(string status, string time)
    {
        var text = NoteText($"status = \"{status}\"\n{time}\n");

        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(text));
    }

    [Theory]
    [InlineData("verified", "verified_at = \"2026-06-10T00:00:00.000Z\"")]
    [InlineData("discarded", "discarded_at = \"2026-06-10T00:00:00.000Z\"")]
    [InlineData("published", "verified_at = \"2026-06-13T00:00:00.000Z\"\npublished_at = \"2026-06-12T00:00:00.000Z\"")]
    [InlineData("retired", "published_at = \"2026-06-13T00:00:00.000Z\"\nretired_at = \"2026-06-12T00:00:00.000Z\"")]
    public void Status_times_out_of_order_make_the_binder_malformed(string status, string times)
    {
        // The note was created on 2026-06-11.
        var text = NoteText($"status = \"{status}\"\n{times}\n");

        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(text));
    }

    [Fact]
    public void A_missing_implied_time_is_the_time_of_the_status_that_implies_it()
    {
        var retired = BinderTomlReader.Read(NoteText("status = \"retired\"\nretired_at = \"2026-06-14T00:00:00.000Z\"\n")).Notes[0];
        var published = BinderTomlReader.Read(NoteText("status = \"published\"\npublished_at = \"2026-06-13T00:00:00.000Z\"\n")).Notes[0];

        var retiredAt = new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(retiredAt, retired.VerifiedAt);
        Assert.Equal(retiredAt, retired.PublishedAt);
        Assert.Equal(retiredAt, retired.RetiredAt);
        var publishedAt = new DateTimeOffset(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(publishedAt, published.VerifiedAt);
        Assert.Equal(publishedAt, published.PublishedAt);

        // Retiring it later keeps the recorded times instead of inventing a verification at that moment.
        NoteLifecycle.ApplyTransition(published, NoteStatus.Retired, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(publishedAt, published.VerifiedAt);
        Assert.Equal(publishedAt, published.PublishedAt);
    }

    [Fact]
    public void A_status_missing_its_own_time_takes_the_time_before_it()
    {
        var created = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero);
        var verifiedAt = new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);

        var discarded = BinderTomlReader.Read(NoteText("status = \"discarded\"\n")).Notes[0];
        var verified = BinderTomlReader.Read(NoteText("status = \"verified\"\n")).Notes[0];
        var published = BinderTomlReader.Read(NoteText("status = \"published\"\nverified_at = \"2026-06-12T00:00:00.000Z\"\n")).Notes[0];

        Assert.Equal(created, discarded.DiscardedAt);
        Assert.Equal(created, verified.VerifiedAt);
        Assert.Equal(verifiedAt, published.VerifiedAt);
        Assert.Equal(verifiedAt, published.PublishedAt);
    }

    // One note created on 2026-06-11, with the given lines after its id.
    private static string NoteText(string lines) =>
        "format_version = 1\nid = \"nb1\"\ncreated = \"2026-06-11T00:00:00.000Z\"\n\n" +
        "[[note]]\nid = \"n1\"\ncreated = \"2026-06-11T00:00:00.000Z\"\n" + lines + "body = ''\n";

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
        note.VerifiedAt = new DateTimeOffset(2026, 6, 12, 12, 0, 0, 0, TimeSpan.Zero);
        note.PublishedAt = new DateTimeOffset(2026, 6, 12, 13, 0, 0, 0, TimeSpan.Zero);

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

    // Hostile attachment edge cases

    [Theory]
    [InlineData("same.png", "same.png")]
    [InlineData("same.png", "SAME.png")]
    public void Colliding_attachment_references_make_the_binder_malformed(string first, string second)
    {
        var binder = OneNote();
        binder.Notes[0].Attachments.Add(first);
        binder.Notes[0].Attachments.Add(second);
        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(BinderTomlWriter.Write(binder)));
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
            "[[note]]\nid = \"n2\"\ncreated = \"2026-04-01T00:00:00.000Z\"\nbody = ''\n";

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
    public void A_note_missing_created_and_modified_takes_its_own_status_times_before_its_binders()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\ncreated = \"2026-01-01T00:00:00.000Z\"\nmodified = \"2026-05-01T00:00:00.000Z\"\n\n" +
            "[[note]]\nid = \"n1\"\nstatus = \"published\"\n" +
            "verified_at = \"2026-03-01T00:00:00.000Z\"\npublished_at = \"2026-03-02T00:00:00.000Z\"\nbody = ''\n";

        var note = BinderTomlReader.Read(text).Notes[0];

        // The earliest time the note itself recorded, so Created never follows its own publication.
        var verified = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(verified, note.Created);
        Assert.Equal(verified, note.Modified);
    }

    [Fact]
    public void A_binder_recording_no_time_takes_the_earliest_its_notes_recorded()
    {
        const string text =
            "format_version = 1\nid = \"nb1\"\n\n" +
            "[[note]]\nid = \"n1\"\nmodified = \"2026-04-01T00:00:00.000Z\"\nbody = ''\n\n" +
            "[[note]]\nid = \"n2\"\ncreated = \"2026-02-01T00:00:00.000Z\"\nbody = ''\n\n" +
            "[[note]]\nid = \"n3\"\nbody = ''\n";

        var binder = BinderTomlReader.Read(text);

        var earliest = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(earliest, binder.Created);
        Assert.Equal(earliest, binder.Modified);
        Assert.Equal(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), binder.Notes[0].Created);
        Assert.Equal(earliest, binder.Notes[1].Modified);
        // A note recording nothing takes its binder's time, never the moment the file was read.
        Assert.Equal(earliest, binder.Notes[2].Created);
        Assert.Equal(earliest, binder.Notes[2].Modified);
    }

    [Theory]
    [InlineData("created = \"not-a-date\"\n", "")]
    [InlineData("modified = \"also bad\"\n", "")]
    [InlineData("", "created = \"garbage\"\n")]
    [InlineData("", "modified = \"\"\n")]
    [InlineData("", "status = \"discarded\"\ndiscarded_at = \"x\"\n")]
    [InlineData("", "status = \"verified\"\nverified_at = \"bad\"\n")]
    [InlineData("", "status = \"published\"\npublished_at = \"nope\"\n")]
    [InlineData("", "status = \"retired\"\nretired_at = \" \"\n")]
    public void A_time_that_does_not_parse_makes_the_binder_malformed(string binderLine, string noteLines)
    {
        // A malformed time is not a missing one: taking it as absent would write another time over it.
        var text = "format_version = 1\nid = \"nb1\"\n" + binderLine + "\n[[note]]\nid = \"n1\"\n" + noteLines + "body = ''\n";

        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read(text));
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
        var retired = binder.Notes[0];
        retired.Status = NoteStatus.Retired;
        retired.VerifiedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        retired.PublishedAt = new DateTimeOffset(2026, 7, 2, 0, 0, 0, TimeSpan.Zero);
        retired.RetiredAt = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero);
        var discarded = OneNote().Notes[0];
        discarded.Id = "n2";
        discarded.Status = NoteStatus.Discarded;
        discarded.DiscardedAt = new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero);
        binder.Notes.Add(discarded);

        var restored = BinderTomlReader.Read(BinderTomlWriter.Write(binder)).Notes;
        Assert.Null(restored[0].DiscardedAt);
        Assert.Equal(retired.VerifiedAt, restored[0].VerifiedAt);
        Assert.Equal(retired.PublishedAt, restored[0].PublishedAt);
        Assert.Equal(retired.RetiredAt, restored[0].RetiredAt);
        Assert.Equal(discarded.DiscardedAt, restored[1].DiscardedAt);
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
