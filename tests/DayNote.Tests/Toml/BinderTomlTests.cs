using System;
using DayNote.Core.Models;
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

        Assert.StartsWith("id = ", text);
        var noteStart = text.IndexOf("[[note]]", StringComparison.Ordinal);
        // The binder has no title line (its title is local app state, not stored in the file).
        Assert.DoesNotContain("title =", text[..noteStart]);
        AssertInOrder(text[..noteStart], "id =", "created =", "modified =");
        AssertInOrder(text[noteStart..], "id =", "title =", "created =", "modified =", "status =", "attachments =", "body =");
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
    public void Reader_tolerates_missing_keys_and_falls_back_for_bad_timestamps()
    {
        const string text =
            "id = \"nb1\"\n" +
            "title = \"Hand edited\"\n" +
            "created = \"2026-06-03T14:23:05.482Z\"\n" +
            "\n" +
            "[[note]]\n" +
            "id = \"n1\"\n" +
            "body = '''\nhello\n'''\n";

        var binder = BinderTomlReader.Read(text);

        Assert.Equal("nb1", binder.Id);
        Assert.Equal(new DateTimeOffset(2026, 6, 3, 14, 23, 5, 482, TimeSpan.Zero), binder.Created);
        // No modified key: it must fall back to load time, never default(DateTimeOffset) (year 0001),
        // which would corrupt chronological ordering.
        Assert.True(binder.Modified.Year > 2000);
        Assert.Equal(string.Empty, binder.Notes[0].Title);
        Assert.Equal("hello", binder.Notes[0].Body);
    }

    [Fact]
    public void Reader_matches_keys_case_insensitively()
    {
        const string text = "ID = \"nb1\"\n\n[[NOTE]]\nID = \"n1\"\nTITLE = \"Caps\"\nBODY = ''\n";

        var binder = BinderTomlReader.Read(text);

        Assert.Equal("nb1", binder.Id);
        Assert.Equal("n1", binder.Notes[0].Id);
        Assert.Equal("Caps", binder.Notes[0].Title);
    }

    [Fact]
    public void Reader_drops_empty_attachment_names()
    {
        const string text =
            "id = \"nb1\"\n\n[[note]]\nid = \"n1\"\nattachments = [\"a.png\", \"\", \"b.png\"]\nbody = ''\n";

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
            "id = \"nb1\"\n\n[[note]]\nid = \"n1\"\n" +
            "attachments = [\"a.png\", \"../escape.txt\", \"sub/dir.png\", \"..\", \".\", \"/etc/passwd\"]\n" +
            "body = ''\n";

        var binder = BinderTomlReader.Read(text);

        Assert.Equal(new[] { "a.png" }, binder.Notes[0].Attachments);
    }

    [Theory]
    [InlineData(NoteStatus.Draft, "draft")]
    [InlineData(NoteStatus.Checked, "checked")]
    [InlineData(NoteStatus.Published, "published")]
    [InlineData(NoteStatus.Expired, "expired")]
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
        const string missing = "id = \"nb1\"\n\n[[note]]\nid = \"n1\"\nbody = ''\n";
        const string unknown = "id = \"nb1\"\n\n[[note]]\nid = \"n1\"\nstatus = \"archived\"\nbody = ''\n";

        Assert.Equal(NoteStatus.Draft, BinderTomlReader.Read(missing).Notes[0].Status);
        Assert.Equal(NoteStatus.Draft, BinderTomlReader.Read(unknown).Notes[0].Status);
    }

    [Fact]
    public void Reader_parses_status_case_insensitively()
    {
        const string text = "id = \"nb1\"\n\n[[note]]\nid = \"n1\"\nstatus = \"Published\"\nbody = ''\n";

        Assert.Equal(NoteStatus.Published, BinderTomlReader.Read(text).Notes[0].Status);
    }

    [Fact]
    public void Reader_throws_a_format_exception_on_invalid_toml()
    {
        Assert.Throws<BinderFormatException>(() => BinderTomlReader.Read("this is = = not [[[ valid"));
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
            Status = NoteStatus.Checked,
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
