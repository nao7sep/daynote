using System;
using DayNote.Core.Models;
using DayNote.Desktop.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

/// <summary>
/// A notes-list row labels each note: its title, or — until one is set — a single-line body preview
/// so an untitled note is still recognizable, with the lifecycle status and creation time alongside.
/// </summary>
public sealed class NoteListItemViewModelTests
{
    private static Note Note(string title = "", string body = "", NoteStatus status = NoteStatus.Draft) => new()
    {
        Id = "n1",
        Title = title,
        Body = body,
        Status = status,
        Created = new DateTimeOffset(2026, 6, 11, 9, 0, 0, TimeSpan.Zero),
        Modified = new DateTimeOffset(2026, 6, 11, 9, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Title_uses_the_note_title_when_set()
    {
        var item = new NoteListItemViewModel(Note(title: "My note"), "UTC");

        Assert.Equal("My note", item.Title);
    }

    [Fact]
    public void Untitled_note_previews_the_body_on_a_single_line()
    {
        var item = new NoteListItemViewModel(Note(body: "  first line\nsecond line  "), "UTC");

        Assert.Equal("first line second line", item.Title);
    }

    [Fact]
    public void Untitled_empty_note_falls_back_to_a_placeholder()
    {
        var item = new NoteListItemViewModel(Note(), "UTC");

        Assert.Equal("(untitled)", item.Title);
    }

    [Theory]
    [InlineData(NoteStatus.Draft, "Draft")]
    [InlineData(NoteStatus.Checked, "Checked")]
    [InlineData(NoteStatus.Published, "Published")]
    [InlineData(NoteStatus.Expired, "Expired")]
    public void StatusLabel_matches_the_lifecycle_state(NoteStatus status, string label)
    {
        var item = new NoteListItemViewModel(Note(status: status), "UTC");

        Assert.Equal(label, item.StatusLabel);
    }

    [Fact]
    public void Subtitle_shows_the_creation_time_in_the_display_zone()
    {
        var item = new NoteListItemViewModel(Note(), "UTC");

        Assert.Equal("2026-06-11 09:00:00", item.Subtitle);
    }

    [Fact]
    public void Refresh_picks_up_a_changed_title()
    {
        var note = Note(title: "Old");
        var item = new NoteListItemViewModel(note, "UTC");

        note.Title = "New";
        item.Refresh();

        Assert.Equal("New", item.Title);
    }
}
