using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DayNote.I18n;
using DayNote.Logging;

namespace DayNote.ViewModels;

public enum RecordsListState
{
    Loading,
    Failed,
    Ready,
}

public enum RecordDetailState
{
    None,
    Loading,
    Failed,
    Ready,
}

/// <summary>
/// The records window: the records newest first, a page at a time, filtered by launch, level and
/// search; the selected record whole; and new records as they are stored.
/// </summary>
/// <remarks>
/// Every read goes through <see cref="IRecordsSource"/>, off the UI thread and within a bound, and a
/// result applies only while the filters it was read for are still the newest asked for. A read
/// that fails is logged, and that log line is itself a stored record, so live reads stop after a
/// failure and resume once a read succeeds.
/// </remarks>
public sealed partial class RecordsWindowViewModel : ObservableObject, IDisposable
{
    internal static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(300);

    // New records are read at most this often while they keep arriving.
    internal static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1);

    private sealed record Filters(string? Session, RecordLevelFilter? Level, string Search);

    private readonly IRecordsSource _source;
    private readonly IRecordsWindowHost _host;
    private readonly IAppLogger _log;
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private readonly RecordFilterOption<string> _allLaunches;

    private Filters _filters = new(null, null, string.Empty);

    // Bumped whenever the filters change, so a page read for older filters is dropped.
    private long _generation;
    private long _detailRequest;
    private bool _more;

    // The busy claim for the next page (PLAYBOOK, Own the work in flight).
    private bool _fetchingMore;

    // New records arrived while the list was scrolled away from the top.
    private bool _newestPending;
    private bool _liveSuspended;
    private bool _atTop = true;
    private bool _started;
    private bool _disposed;
    private IDisposable? _searchTimer;
    private IDisposable? _liveTimer;

    public RecordsWindowViewModel(
        IRecordsSource source,
        IRecordsWindowHost host,
        IAppLogger log,
        Func<TimeSpan, Action, IDisposable>? schedule = null)
    {
        _source = source;
        _host = host;
        _log = log;
        _schedule = schedule ?? ((delay, action) => DispatcherTimer.RunOnce(action, delay));

        _allLaunches = new RecordFilterOption<string>(null, () => Localizer.T("records.allLaunches"));
        LaunchOptions = [_allLaunches];
        _selectedLaunch = _allLaunches;

        var allLevels = new RecordFilterOption<RecordLevelFilter?>(null, () => Localizer.T("records.allLevels"));
        LevelOptions =
        [
            allLevels,
            .. RecordFormat.LevelFilters.Select(level =>
                new RecordFilterOption<RecordLevelFilter?>(level, () => Localizer.T(RecordFormat.LevelFilterKey(level)))),
        ];
        _selectedLevel = allLevels;

        Records.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ListNote));
    }

    public ObservableCollection<RecordRowViewModel> Records { get; } = [];

    public ObservableCollection<RecordFilterOption<string>> LaunchOptions { get; }

    public IReadOnlyList<RecordFilterOption<RecordLevelFilter?>> LevelOptions { get; }

    [ObservableProperty]
    private RecordFilterOption<string> _selectedLaunch;

    [ObservableProperty]
    private RecordFilterOption<RecordLevelFilter?> _selectedLevel;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListNote), nameof(IsListNoteError))]
    private RecordsListState _listState = RecordsListState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndNote))]
    private bool _isLoadingMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndNote))]
    private bool _moreFailed;

    [ObservableProperty]
    private RecordRowViewModel? _selectedRecord;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailNote), nameof(IsDetailNoteError), nameof(HasDetail))]
    private RecordDetailState _detailState;

    [ObservableProperty]
    private RecordDetailViewModel? _detail;

    /// <summary>What the list's body says instead of rows: loading, a failure, or no match.</summary>
    public string? ListNote => ListState switch
    {
        RecordsListState.Loading => Localizer.T("records.loading"),
        RecordsListState.Failed => Localizer.T("records.loadFailed"),
        _ when Records.Count == 0 => Localizer.T("records.empty"),
        _ => null,
    };

    public bool IsListNoteError => ListState == RecordsListState.Failed;

    /// <summary>What the end of the list says while the next page loads or after it failed.</summary>
    public string? EndNote =>
        MoreFailed ? Localizer.T("records.loadFailed")
        : IsLoadingMore ? Localizer.T("records.loading")
        : null;

    public string? DetailNote => DetailState switch
    {
        RecordDetailState.None => Localizer.T("records.noSelection"),
        RecordDetailState.Failed => Localizer.T("records.detailFailed"),
        _ => null,
    };

    public bool IsDetailNoteError => DetailState == RecordDetailState.Failed;

    public bool HasDetail => DetailState == RecordDetailState.Ready;

    /// <summary>The list pane's width to open at.</summary>
    public double ListWidth => _host.RecordsListWidth;

    public WindowPlacement? Placement => _host.RecordsWindowPlacement;

    public void SaveListWidth(double width) => _host.SaveRecordsListWidth(width);

    public void SavePlacement(WindowPlacement placement) => _host.SaveRecordsWindowPlacement(placement);

    /// <summary>Reads the first page and the launches, and follows new records from now on.</summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _source.Stored += OnSourceStored;
        _ = ReadSessionsAsync();
        _ = ReadFirstPageAsync();
    }

    /// <summary>
    /// The list's scroll position after it moved or its content changed: whether it is at the top,
    /// whether its end is within about a screen, and whether the reader moved it.
    /// </summary>
    public void ViewportChanged(bool atTop, bool nearEnd, bool byReader)
    {
        _atTop = atTop;
        if (atTop && _newestPending)
        {
            _newestPending = false;
            _ = ReadNewestAsync();
        }

        if (nearEnd)
        {
            // A failed page waits for the reader to reach the end again.
            _ = LoadMoreAsync(retryFailed: byReader);
        }
    }

    /// <summary>Brings every word on screen into the current language.</summary>
    public void Retranslate()
    {
        foreach (var option in LaunchOptions)
        {
            option.Refresh();
        }

        foreach (var option in LevelOptions)
        {
            option.Refresh();
        }

        foreach (var row in Records)
        {
            row.Refresh(_host.DisplayZone);
        }

        if (Detail is { } detail)
        {
            Detail = new RecordDetailViewModel(detail.Record, _source.Session, _host.DisplayZone);
        }

        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;
        _detailRequest++;
        _source.Stored -= OnSourceStored;
        _searchTimer?.Dispose();
        _liveTimer?.Dispose();
    }

    partial void OnSelectedLaunchChanged(RecordFilterOption<string> value) =>
        ApplyFilters(_filters with { Session = value?.Value });

    partial void OnSelectedLevelChanged(RecordFilterOption<RecordLevelFilter?> value) =>
        ApplyFilters(_filters with { Level = value?.Value });

    partial void OnSearchTextChanged(string value)
    {
        _searchTimer?.Dispose();
        _searchTimer = _schedule(SearchDelay, () =>
        {
            _searchTimer = null;
            ApplyFilters(_filters with { Search = SearchText });
        });
    }

    partial void OnSelectedRecordChanged(RecordRowViewModel? value)
    {
        var request = ++_detailRequest;
        if (value is null)
        {
            Detail = null;
            DetailState = RecordDetailState.None;
            return;
        }

        if (Detail?.Record.Id != value.Record.Id)
        {
            Detail = null;
            DetailState = RecordDetailState.Loading;
            _ = ReadDetailAsync(value.Record.Id, request);
        }

        // Keyboard or pointer reaching the last row reaches the end of what is loaded.
        if (ReferenceEquals(value, Records.LastOrDefault()))
        {
            _ = LoadMoreAsync(retryFailed: true);
        }
    }

    private void ApplyFilters(Filters next)
    {
        if (next == _filters || _disposed)
        {
            return;
        }

        _filters = next;
        if (_started)
        {
            _ = ReadFirstPageAsync();
        }
    }

    private RecordsQuery Query(Filters filters, RecordCursor? after) =>
        new(filters.Session, filters.Level, filters.Search, after);

    private async Task ReadFirstPageAsync()
    {
        var generation = ++_generation;
        var filters = _filters;
        var selectedId = SelectedRecord?.Record.Id;
        _fetchingMore = false;
        _newestPending = false;
        _more = false;
        IsLoadingMore = false;
        MoreFailed = false;
        Records.Clear();
        ListState = RecordsListState.Loading;

        RecordsPage page;
        try
        {
            page = await _source.ReadPageAsync(Query(filters, null));
        }
        catch (Exception ex)
        {
            if (generation != _generation)
            {
                return;
            }

            ReadFailed("page", ex);
            ListState = RecordsListState.Failed;
            return;
        }

        if (generation != _generation)
        {
            return;
        }

        _liveSuspended = false;
        // A live read may have filled the list while this page was read; this page replaces it.
        Records.Clear();
        foreach (var record in page.Records)
        {
            Records.Add(new RecordRowViewModel(record, _host.DisplayZone));
        }

        _more = page.More;
        ListState = RecordsListState.Ready;

        // A record still listed under the new filters stays selected.
        if (selectedId is { } id && Records.FirstOrDefault(row => row.Record.Id == id) is { } row)
        {
            SelectedRecord = row;
        }
    }

    private async Task LoadMoreAsync(bool retryFailed)
    {
        if (ListState != RecordsListState.Ready || !_more || _fetchingMore || (MoreFailed && !retryFailed) || _disposed)
        {
            return;
        }

        _fetchingMore = true;
        var generation = _generation;
        var filters = _filters;
        var after = RecordsPaging.CursorAfter(Records.Select(row => row.Record).ToList());
        IsLoadingMore = true;
        MoreFailed = false;

        RecordsPage page;
        try
        {
            page = await _source.ReadPageAsync(Query(filters, after));
        }
        catch (Exception ex)
        {
            if (generation != _generation)
            {
                return;
            }

            _fetchingMore = false;
            ReadFailed("next page", ex);
            IsLoadingMore = false;
            MoreFailed = true;
            return;
        }

        if (generation != _generation)
        {
            return;
        }

        _fetchingMore = false;
        _liveSuspended = false;
        foreach (var record in page.Records)
        {
            Records.Add(new RecordRowViewModel(record, _host.DisplayZone));
        }

        _more = page.More;
        IsLoadingMore = false;
    }

    // The newest page read again for new records. It joins the rows already shown rather than
    // replacing them, so the list never falls back to the loading note, the pages already read
    // stay, and the selected row stays selected.
    private async Task ReadNewestAsync()
    {
        var generation = _generation;
        RecordsPage page;
        try
        {
            page = await _source.ReadPageAsync(Query(_filters, null));
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                ReadFailed("newest page", ex);
            }

            return;
        }

        if (generation != _generation)
        {
            return;
        }

        _liveSuspended = false;
        if (ListState != RecordsListState.Ready)
        {
            Records.Clear();
            foreach (var record in page.Records)
            {
                Records.Add(new RecordRowViewModel(record, _host.DisplayZone));
            }

            _more = page.More;
            MoreFailed = false;
            ListState = RecordsListState.Ready;
            return;
        }

        var (merged, more) = RecordsPaging.MergeNewest(Records.Select(row => row.Record).ToList(), _more, page);
        // Every row shown is in the merge, in the same order, so new rows are only inserted.
        for (var index = 0; index < merged.Count; index++)
        {
            if (index >= Records.Count || Records[index].Record.Id != merged[index].Id)
            {
                Records.Insert(index, new RecordRowViewModel(merged[index], _host.DisplayZone));
            }
        }

        _more = more;
    }

    private async Task ReadSessionsAsync()
    {
        IReadOnlyList<string> sessions;
        try
        {
            sessions = await _source.ReadSessionsAsync();
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                ReadFailed("launches", ex);
            }

            return;
        }

        if (_disposed)
        {
            return;
        }

        // Launches only ever join the list, newest first, after All; the chosen one stays chosen.
        for (var index = 0; index < sessions.Count; index++)
        {
            var position = index + 1;
            if (position >= LaunchOptions.Count || LaunchOptions[position].Value != sessions[index])
            {
                var session = sessions[index];
                LaunchOptions.Insert(position, new RecordFilterOption<string>(session, () => LaunchLabel(session)));
            }
        }
    }

    private async Task ReadDetailAsync(long id, long request)
    {
        RecordDetail? record;
        try
        {
            record = await _source.ReadDetailAsync(id);
        }
        catch (Exception ex)
        {
            if (request != _detailRequest)
            {
                return;
            }

            _log.Warn("Records read failed", new { read = "record", id }, ex);
            DetailState = RecordDetailState.Failed;
            return;
        }

        if (request != _detailRequest)
        {
            return;
        }

        if (record is null)
        {
            DetailState = RecordDetailState.Failed;
            return;
        }

        Detail = new RecordDetailViewModel(record, _source.Session, _host.DisplayZone);
        DetailState = RecordDetailState.Ready;
    }

    private void OnSourceStored() => Dispatcher.UIThread.Post(OnStored);

    // A stored record reaches the list at once while it is scrolled to the top; otherwise it waits
    // until the list is back there, so the list never moves under the reader.
    internal void OnStored()
    {
        if (_disposed || _liveTimer is not null || _liveSuspended)
        {
            return;
        }

        _liveTimer = _schedule(LiveInterval, () =>
        {
            _liveTimer = null;
            if (_disposed)
            {
                return;
            }

            _ = ReadSessionsAsync();
            if (_atTop)
            {
                _ = ReadNewestAsync();
            }
            else
            {
                _newestPending = true;
            }
        });
    }

    private void ReadFailed(string read, Exception error)
    {
        _liveSuspended = true;
        _log.Warn("Records read failed", new { read }, error);
    }

    private string LaunchLabel(string session) => RecordFormat.LaunchText(session, _source.Session, _host.DisplayZone);
}
