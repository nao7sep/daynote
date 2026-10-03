using System.Linq;
using DayNote.Logging;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class RecordsPagingTests
{
    private static RecordSummary Row(long id, int second) =>
        new(id, $"2026-10-04T10:00:{second:00}.000Z", "s", "info", $"m{id}", null);

    [Fact]
    public void The_next_page_starts_after_the_last_record_shown()
    {
        Assert.Null(RecordsPaging.CursorAfter([]));
        Assert.Equal(new RecordCursor(Row(3, 1).Time, 3), RecordsPaging.CursorAfter([Row(5, 2), Row(3, 1)]));
    }

    [Fact]
    public void Newest_first_orders_by_time_then_by_id()
    {
        var rows = new[] { Row(1, 5), Row(3, 5), Row(2, 9) }.ToList();
        rows.Sort(RecordsPaging.NewestFirst);
        Assert.Equal([2L, 3L, 1L], rows.Select(row => row.Id));
    }

    [Fact]
    public void New_records_join_the_top_and_every_row_already_shown_stays()
    {
        // Two pages shown; the newest page read again overlaps the first and adds two.
        var shown = Enumerable.Range(1, 6).Select(index => Row(7 - index, 7 - index)).ToList();
        var page = new RecordsPage([Row(8, 8), Row(7, 7), Row(6, 6), Row(5, 5)], More: true);

        var (records, more) = RecordsPaging.MergeNewest(shown, shownMore: false, page);

        Assert.Equal([8L, 7L, 6L, 5L, 4L, 3L, 2L, 1L], records.Select(row => row.Id));
        // The rows beyond the page were shown, so whether more follow is still theirs to say.
        Assert.False(more);
    }

    [Fact]
    public void A_page_that_reaches_past_every_row_shown_says_whether_more_follow()
    {
        var shown = new[] { Row(2, 2), Row(1, 1) };
        var page = new RecordsPage([Row(3, 3), Row(2, 2), Row(1, 1)], More: true);

        var (records, more) = RecordsPaging.MergeNewest(shown, shownMore: false, page);

        Assert.Equal([3L, 2L, 1L], records.Select(row => row.Id));
        Assert.True(more);
    }

    [Fact]
    public void A_row_in_both_keeps_the_copy_already_shown()
    {
        var shownRow = Row(1, 1);
        var (records, _) = RecordsPaging.MergeNewest([shownRow], false, new RecordsPage([Row(1, 1) with { }], false));

        Assert.Same(shownRow, Assert.Single(records));
    }
}
