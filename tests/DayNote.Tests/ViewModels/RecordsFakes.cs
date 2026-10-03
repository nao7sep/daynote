using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DayNote.Logging;
using DayNote.ViewModels;

namespace DayNote.Tests.ViewModels;

/// <summary>
/// Records held in memory for the records window's tests. A read answers at once from
/// <see cref="Records"/>, or, while <see cref="Hold"/> is set, waits in <see cref="Pending"/> until the
/// test settles it, so a test can order results and failures as it needs.
/// </summary>
internal sealed class FakeRecordsSource : IRecordsSource
{
    internal const int PageSize = 100;

    public string Session { get; set; } = "2026-10-04T09:00:00.000Z";

    public List<RecordDetail> Records { get; } = [];

    public List<RecordsQuery> PageQueries { get; } = [];

    public List<TaskCompletionSource<RecordsPage>> Pending { get; } = [];

    public bool Hold { get; set; }

    public Exception? PageFailure { get; set; }

    public Exception? DetailFailure { get; set; }

    public int DetailReads { get; private set; }

    public int SessionReads { get; private set; }

    public int Subscribers => Stored?.GetInvocationList().Length ?? 0;

    public event Action? Stored;

    public void RaiseStored() => Stored?.Invoke();

    /// <summary>Adds <paramref name="count"/> records, newest last, one second apart.</summary>
    public void Add(int count, string level = "info", string session = "2026-10-04T09:00:00.000Z")
    {
        var start = Records.Count;
        for (var index = start; index < start + count; index++)
        {
            Records.Add(new RecordDetail(
                index + 1,
                new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero).AddSeconds(index).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                session,
                level,
                $"Message {index + 1}",
                null,
                $$"""{"index":{{index + 1}}}"""));
        }
    }

    public Task<RecordsPage> ReadPageAsync(RecordsQuery query)
    {
        PageQueries.Add(query);
        if (Hold)
        {
            var pending = new TaskCompletionSource<RecordsPage>();
            Pending.Add(pending);
            return pending.Task;
        }

        return PageFailure is { } failure ? Task.FromException<RecordsPage>(failure) : Task.FromResult(Answer(query));
    }

    /// <summary>The page the records give <paramref name="query"/> now.</summary>
    public RecordsPage Answer(RecordsQuery query)
    {
        var matching = Records
            .Select(Summary)
            .Where(record => query.Session is null || record.Session == query.Session)
            .Where(record => query.Level switch
            {
                null => true,
                RecordLevelFilter.Attention => record.Level is "warn" or "error",
                { } level => record.Level == level.ToString().ToLowerInvariant(),
            })
            .Where(record => record.Message.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record, Comparer<RecordSummary>.Create(RecordsPaging.NewestFirst))
            .Where(record => query.After is not { } after
                || string.CompareOrdinal(record.Time, after.Time) < 0
                || (record.Time == after.Time && record.Id < after.Id))
            .ToList();
        return new RecordsPage(matching.Take(PageSize).ToList(), matching.Count > PageSize);
    }

    public Task<RecordDetail?> ReadDetailAsync(long id)
    {
        DetailReads++;
        return DetailFailure is { } failure
            ? Task.FromException<RecordDetail?>(failure)
            : Task.FromResult(Records.FirstOrDefault(record => record.Id == id));
    }

    public Task<IReadOnlyList<string>> ReadSessionsAsync()
    {
        SessionReads++;
        return Task.FromResult<IReadOnlyList<string>>(
            Records.Select(record => record.Session).Distinct().OrderByDescending(session => session, StringComparer.Ordinal).ToList());
    }

    private static RecordSummary Summary(RecordDetail record) =>
        new(record.Id, record.Time, record.Session, record.Level, record.Message, record.NoteId);
}

/// <summary>The records window's layout and display zone, kept in memory.</summary>
internal sealed class FakeRecordsHost : IRecordsWindowHost
{
    public double RecordsListWidth { get; set; } = 380;

    public List<double> SavedListWidths { get; } = [];

    public WindowPlacement? RecordsWindowPlacement { get; set; }

    public List<WindowPlacement> SavedPlacements { get; } = [];

    public TimeZoneInfo DisplayZone { get; set; } = TimeZoneInfo.Utc;

    public void SaveRecordsListWidth(double width)
    {
        RecordsListWidth = width;
        SavedListWidths.Add(width);
    }

    public void SaveRecordsWindowPlacement(WindowPlacement placement)
    {
        RecordsWindowPlacement = placement;
        SavedPlacements.Add(placement);
    }
}

/// <summary>Timers a test fires by hand.</summary>
internal sealed class ManualScheduler
{
    private readonly List<Entry> _entries = [];

    public int Pending => _entries.Count(entry => !entry.Cancelled);

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var entry = new Entry(delay, action);
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Fires every timer still set, including any a fired one sets.</summary>
    public void RunAll()
    {
        while (_entries.FirstOrDefault(entry => !entry.Cancelled) is { } next)
        {
            _entries.Remove(next);
            next.Action();
        }
    }

    public IEnumerable<TimeSpan> Delays => _entries.Where(entry => !entry.Cancelled).Select(entry => entry.Delay);

    private sealed class Entry(TimeSpan delay, Action action) : IDisposable
    {
        public TimeSpan Delay { get; } = delay;
        public Action Action { get; } = action;
        public bool Cancelled { get; private set; }
        public void Dispose() => Cancelled = true;
    }
}
