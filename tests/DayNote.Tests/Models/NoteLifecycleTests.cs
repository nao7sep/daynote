using System;
using DayNote.Core.Models;
using Xunit;

namespace DayNote.Tests.Models;

/// <summary>
/// The transition table of the content-lifecycle-conventions, row by row: each pair of statuses in
/// status order, both directions. A cell is <c>now</c> (the moment of the move), <c>keep</c> (the time
/// the note already had) or <c>clear</c> (empty).
/// </summary>
public sealed class NoteLifecycleTests
{
    private static readonly DateTimeOffset T1 = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 6, 10, 13, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 6, 10, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 6, 11, 9, 0, 0, TimeSpan.Zero);

    // A note in each status, holding exactly the times that status holds.
    private static Note InStatus(NoteStatus status)
    {
        var note = new Note { Id = "n1", Status = status };
        switch (status)
        {
            case NoteStatus.Discarded:
                note.DiscardedAt = T1;
                break;
            case NoteStatus.Verified:
                note.VerifiedAt = T1;
                break;
            case NoteStatus.Published:
                note.VerifiedAt = T1;
                note.PublishedAt = T2;
                break;
            case NoteStatus.Retired:
                note.VerifiedAt = T1;
                note.PublishedAt = T2;
                note.RetiredAt = T3;
                break;
        }

        return note;
    }

    [Theory]
    // Columns: from, to, then discarded, verified, published, retired.
    [InlineData(NoteStatus.Draft, NoteStatus.Discarded, "now", "clear", "clear", "clear")]          // 1
    [InlineData(NoteStatus.Discarded, NoteStatus.Draft, "clear", "clear", "clear", "clear")]        // 2
    [InlineData(NoteStatus.Draft, NoteStatus.Verified, "clear", "now", "clear", "clear")]           // 3
    [InlineData(NoteStatus.Verified, NoteStatus.Draft, "clear", "clear", "clear", "clear")]         // 4
    [InlineData(NoteStatus.Draft, NoteStatus.Published, "clear", "now", "now", "clear")]            // 5
    [InlineData(NoteStatus.Published, NoteStatus.Draft, "clear", "clear", "clear", "clear")]        // 6
    [InlineData(NoteStatus.Draft, NoteStatus.Retired, "clear", "now", "now", "now")]                // 7
    [InlineData(NoteStatus.Retired, NoteStatus.Draft, "clear", "clear", "clear", "clear")]          // 8
    [InlineData(NoteStatus.Discarded, NoteStatus.Verified, "clear", "now", "clear", "clear")]       // 9
    [InlineData(NoteStatus.Verified, NoteStatus.Discarded, "now", "clear", "clear", "clear")]       // 10
    [InlineData(NoteStatus.Discarded, NoteStatus.Published, "clear", "now", "now", "clear")]        // 11
    [InlineData(NoteStatus.Published, NoteStatus.Discarded, "now", "clear", "clear", "clear")]      // 12
    [InlineData(NoteStatus.Discarded, NoteStatus.Retired, "clear", "now", "now", "now")]            // 13
    [InlineData(NoteStatus.Retired, NoteStatus.Discarded, "now", "clear", "clear", "clear")]        // 14
    [InlineData(NoteStatus.Verified, NoteStatus.Published, "clear", "keep", "now", "clear")]        // 15
    [InlineData(NoteStatus.Published, NoteStatus.Verified, "clear", "keep", "clear", "clear")]      // 16
    [InlineData(NoteStatus.Verified, NoteStatus.Retired, "clear", "keep", "now", "now")]            // 17
    [InlineData(NoteStatus.Retired, NoteStatus.Verified, "clear", "keep", "clear", "clear")]        // 18
    [InlineData(NoteStatus.Published, NoteStatus.Retired, "clear", "keep", "keep", "now")]          // 19
    [InlineData(NoteStatus.Retired, NoteStatus.Published, "clear", "keep", "keep", "clear")]        // 20
    public void Every_transition_follows_the_table(
        NoteStatus from, NoteStatus to, string discarded, string verified, string published, string retired)
    {
        var note = InStatus(from);
        var before = InStatus(from);

        NoteLifecycle.ApplyTransition(note, to, Now);

        Assert.Equal(to, note.Status);
        Assert.Equal(Expected(discarded, before.DiscardedAt), note.DiscardedAt);
        Assert.Equal(Expected(verified, before.VerifiedAt), note.VerifiedAt);
        Assert.Equal(Expected(published, before.PublishedAt), note.PublishedAt);
        Assert.Equal(Expected(retired, before.RetiredAt), note.RetiredAt);
    }

    private static DateTimeOffset? Expected(string cell, DateTimeOffset? before) => cell switch
    {
        "now" => Now,
        "keep" => Assert.IsType<DateTimeOffset>(before),
        "clear" => null,
        _ => throw new ArgumentOutOfRangeException(nameof(cell), cell, null),
    };

    [Theory]
    [InlineData(NoteStatus.Draft)]
    [InlineData(NoteStatus.Discarded)]
    [InlineData(NoteStatus.Verified)]
    [InlineData(NoteStatus.Published)]
    [InlineData(NoteStatus.Retired)]
    public void Selecting_the_current_status_changes_nothing(NoteStatus status)
    {
        var note = InStatus(status);
        var before = InStatus(status);

        NoteLifecycle.ApplyTransition(note, status, Now);

        Assert.Equal(before.DiscardedAt, note.DiscardedAt);
        Assert.Equal(before.VerifiedAt, note.VerifiedAt);
        Assert.Equal(before.PublishedAt, note.PublishedAt);
        Assert.Equal(before.RetiredAt, note.RetiredAt);
    }

    [Fact]
    public void A_new_time_never_precedes_a_kept_time_it_follows()
    {
        // The clock stepped back behind the kept verification time.
        var note = InStatus(NoteStatus.Verified);
        var earlier = T1.AddMinutes(-5);

        NoteLifecycle.ApplyTransition(note, NoteStatus.Retired, earlier);

        Assert.Equal(T1, note.VerifiedAt);
        Assert.Equal(T1, note.PublishedAt);
        Assert.Equal(T1, note.RetiredAt);
    }
}
