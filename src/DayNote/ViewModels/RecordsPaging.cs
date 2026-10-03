using DayNote.Logging;

namespace DayNote.ViewModels;

/// <summary>
/// The list's paging decisions, kept apart from the window: where the next page starts, and how the
/// newest page read again joins the rows already shown.
/// </summary>
public static class RecordsPaging
{
    /// <summary>The page after the last record shown, or the first page when none is.</summary>
    public static RecordCursor? CursorAfter(IReadOnlyList<RecordSummary> records) =>
        records.Count == 0 ? null : new RecordCursor(records[^1].Time, records[^1].Id);

    /// <summary>
    /// The order the list shows records in, newest first, which is the order the database pages them
    /// in: by time, then by id. A negative result puts <paramref name="a"/> first.
    /// </summary>
    public static int NewestFirst(RecordSummary a, RecordSummary b)
    {
        var time = string.CompareOrdinal(b.Time, a.Time);
        return time != 0 ? time : b.Id.CompareTo(a.Id);
    }

    /// <summary>
    /// The newest page read again, joined with the rows already shown: a row in both keeps the
    /// shown copy, and the rows shown beyond the page stay, so the pages already read are kept and a
    /// page read out of order loses nothing. More follow when they followed the rows shown beyond the
    /// page, or, when the page reaches past every row shown, when they follow the page.
    /// </summary>
    public static (IReadOnlyList<RecordSummary> Records, bool More) MergeNewest(
        IReadOnlyList<RecordSummary> shown, bool shownMore, RecordsPage page)
    {
        var byId = new Dictionary<long, RecordSummary>();
        foreach (var record in shown)
        {
            byId[record.Id] = record;
        }

        foreach (var record in page.Records)
        {
            byId.TryAdd(record.Id, record);
        }

        var records = byId.Values.ToList();
        records.Sort(NewestFirst);

        var beyond = page.Records.Count > 0
            && shown.Any(record => NewestFirst(record, page.Records[^1]) > 0);
        return (records, beyond ? shownMore : page.More);
    }
}
