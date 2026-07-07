using System;
using DayNote.Core.Models;
using Xunit;

namespace DayNote.Tests.Models;

public sealed class NoteLifecycleTests
{
    private static readonly DateTimeOffset T1 = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 6, 10, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 6, 10, 14, 0, 0, TimeSpan.Zero);

    private static Note NewNote() => new() { Id = "n1" };

    [Fact]
    public void Draft_to_ready_sets_ReadyAt()
    {
        var note = NewNote();
        NoteLifecycle.ApplyTransition(note, NoteStatus.Ready, T1);

        Assert.Equal(NoteStatus.Ready, note.Status);
        Assert.Equal(T1, note.ReadyAt);
        Assert.Null(note.PublishedAt);
        Assert.Null(note.ExpiredAt);
    }

    [Fact]
    public void Draft_to_published_sets_ReadyAt_and_PublishedAt()
    {
        var note = NewNote();
        NoteLifecycle.ApplyTransition(note, NoteStatus.Published, T1);

        Assert.Equal(T1, note.ReadyAt);
        Assert.Equal(T1, note.PublishedAt);
        Assert.Null(note.ExpiredAt);
    }

    [Fact]
    public void Draft_to_expired_sets_all_three_timestamps()
    {
        var note = NewNote();
        NoteLifecycle.ApplyTransition(note, NoteStatus.Expired, T1);

        Assert.Equal(T1, note.ReadyAt);
        Assert.Equal(T1, note.PublishedAt);
        Assert.Equal(T1, note.ExpiredAt);
    }

    [Fact]
    public void Return_to_draft_clears_all_timestamps()
    {
        var note = NewNote();
        NoteLifecycle.ApplyTransition(note, NoteStatus.Published, T1);
        NoteLifecycle.ApplyTransition(note, NoteStatus.Draft, T2);

        Assert.Equal(NoteStatus.Draft, note.Status);
        Assert.Null(note.ReadyAt);
        Assert.Null(note.PublishedAt);
        Assert.Null(note.ExpiredAt);
    }

    [Fact]
    public void Published_to_ready_to_published_preserves_original_times()
    {
        var note = NewNote();
        NoteLifecycle.ApplyTransition(note, NoteStatus.Published, T1);

        NoteLifecycle.ApplyTransition(note, NoteStatus.Ready, T2);
        Assert.Equal(T1, note.ReadyAt);
        Assert.Equal(T1, note.PublishedAt);

        NoteLifecycle.ApplyTransition(note, NoteStatus.Published, T3);
        Assert.Equal(T1, note.ReadyAt);
        Assert.Equal(T1, note.PublishedAt);
    }

    [Fact]
    public void Expired_to_published_preserves_all_original_times()
    {
        var note = NewNote();
        NoteLifecycle.ApplyTransition(note, NoteStatus.Expired, T1);
        NoteLifecycle.ApplyTransition(note, NoteStatus.Published, T2);

        Assert.Equal(T1, note.ReadyAt);
        Assert.Equal(T1, note.PublishedAt);
        Assert.Equal(T1, note.ExpiredAt);
    }

    [Fact]
    public void Ready_to_published_only_sets_PublishedAt()
    {
        var note = NewNote();
        NoteLifecycle.ApplyTransition(note, NoteStatus.Ready, T1);
        NoteLifecycle.ApplyTransition(note, NoteStatus.Published, T2);

        Assert.Equal(T1, note.ReadyAt);
        Assert.Equal(T2, note.PublishedAt);
    }

    [Fact]
    public void Expired_to_draft_clears_everything()
    {
        var note = NewNote();
        NoteLifecycle.ApplyTransition(note, NoteStatus.Expired, T1);
        NoteLifecycle.ApplyTransition(note, NoteStatus.Draft, T2);

        Assert.Null(note.ReadyAt);
        Assert.Null(note.PublishedAt);
        Assert.Null(note.ExpiredAt);
    }
}
