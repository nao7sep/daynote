using System;
using DayNote.Core.Models;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

/// <summary>
/// The editor view model writes edits straight back to the underlying note and drives the dirty/
/// autosave path through its <c>Edited</c> event, so loading must not look like an edit, status must
/// gate editability, and the title/counts must behave at the commit boundary.
/// </summary>
public sealed class EditorViewModelTests
{
    private static EditorViewModel NewEditor() => new(TimeZoneInfo.Utc);

    private static Note NewNote(string title = "", string body = "", NoteStatus status = NoteStatus.Draft) => new()
    {
        Id = "n1",
        Title = title,
        Body = body,
        Status = status,
        Created = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
        Modified = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Load_populates_fields_without_raising_Changed()
    {
        var editor = NewEditor();
        var raised = false;
        editor.Changed += (_, _) => raised = true;

        editor.Load(NewNote(title: "Hello", body: "world", status: NoteStatus.Verified));

        Assert.True(editor.HasNote);
        Assert.Equal("Hello", editor.Title);
        Assert.Equal("world", editor.Body);
        Assert.Equal(NoteStatus.Verified, editor.Status);
        Assert.False(raised);
    }

    [Fact]
    public void Load_null_clears_the_editor()
    {
        var editor = NewEditor();
        editor.Load(NewNote(title: "Hello", body: "world"));

        editor.Load(null);

        Assert.False(editor.HasNote);
        Assert.Equal(string.Empty, editor.Title);
        Assert.Equal(string.Empty, editor.Body);
        Assert.Equal(string.Empty, editor.CreatedText);
    }

    [Fact]
    public void Editing_the_title_writes_through_to_the_note_and_raises_Changed()
    {
        var editor = NewEditor();
        var note = NewNote();
        editor.Load(note);
        var raised = 0;
        editor.Changed += (_, _) => raised++;

        editor.Title = "New title";

        Assert.Equal("New title", note.Title);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Editing_the_body_writes_through_and_updates_counts()
    {
        var editor = NewEditor();
        var note = NewNote();
        editor.Load(note);

        editor.Body = "one two three";

        Assert.Equal("one two three", note.Body);
        Assert.Equal("3 words", editor.WordsText);
    }

    [Theory]
    [InlineData(NoteStatus.Draft)]
    [InlineData(NoteStatus.Discarded)]
    [InlineData(NoteStatus.Verified)]
    [InlineData(NoteStatus.Published)]
    [InlineData(NoteStatus.Retired)]
    public void Editability_follows_the_lock_never_the_status(NoteStatus status)
    {
        var editor = NewEditor();

        editor.Load(NewNote(status: status));
        Assert.True(editor.IsEditable);

        var locked = NewNote(status: status);
        locked.Locked = true;
        editor.Load(locked);
        Assert.False(editor.IsEditable);
    }

    [Fact]
    public void Changing_status_writes_through_and_leaves_the_lock_alone()
    {
        var editor = NewEditor();
        var note = NewNote(status: NoteStatus.Draft);
        editor.Load(note);
        var raised = 0;
        editor.Changed += (_, _) => raised++;

        editor.Status = NoteStatus.Published;

        Assert.True(editor.IsEditable);
        Assert.False(note.Locked);
        Assert.Equal(NoteStatus.Published, note.Status);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Locking_writes_through_and_leaves_the_status_and_times_alone()
    {
        var editor = NewEditor();
        var note = NewNote(status: NoteStatus.Published);
        note.PublishedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var modified = note.Modified;
        editor.Load(note);
        var raised = 0;
        editor.Changed += (_, _) => raised++;

        editor.Locked = true;

        Assert.True(note.Locked);
        Assert.False(editor.IsEditable);
        Assert.Equal(NoteStatus.Published, note.Status);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), note.PublishedAt);
        Assert.Equal(modified, note.Modified);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void NormalizeTitle_flattens_to_a_single_line_at_commit()
    {
        var editor = NewEditor();
        var note = NewNote();
        editor.Load(note);

        editor.Title = "  Hello\nWorld  ";
        editor.NormalizeTitle();

        Assert.Equal("Hello World", editor.Title);
        Assert.Equal("Hello World", note.Title);
    }

    [Fact]
    public void NormalizeTitle_is_a_no_op_when_already_clean()
    {
        var editor = NewEditor();
        editor.Load(NewNote(title: "Clean"));
        var raised = 0;
        editor.Changed += (_, _) => raised++;

        editor.NormalizeTitle();

        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData("", "0 words", "0 chars")]
    [InlineData("word", "1 word", "4 chars")]
    [InlineData("two words", "2 words", "9 chars")]
    public void Counts_use_singular_plural_and_invariant_formatting(string body, string words, string chars)
    {
        var editor = NewEditor();
        editor.Load(NewNote(body: body));

        Assert.Equal(words, editor.WordsText);
        Assert.Equal(chars, editor.CharsText);
    }
}
