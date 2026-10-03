using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DayNote.I18n;
using DayNote.Logging;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class RecordsWindowViewModelTests
{
    private readonly FakeRecordsSource _source = new();
    private readonly FakeRecordsHost _host = new();
    private readonly RecordingLogger _log = new();
    private readonly ManualScheduler _timers = new();

    private RecordsWindowViewModel Create() => new(_source, _host, _log, _timers.Schedule);

    private RecordsWindowViewModel Started()
    {
        var vm = Create();
        vm.Start();
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    private static long[] Ids(RecordsWindowViewModel vm) => vm.Records.Select(row => row.Record.Id).ToArray();

    [AvaloniaFact]
    public void The_first_page_shows_the_newest_hundred_with_every_filter_off()
    {
        _source.Add(150);

        var vm = Started();

        Assert.Equal(RecordsListState.Ready, vm.ListState);
        Assert.Equal(100, vm.Records.Count);
        Assert.Equal(150, vm.Records[0].Record.Id);
        Assert.Null(vm.ListNote);
        var query = Assert.Single(_source.PageQueries);
        Assert.Equal(new RecordsQuery(null, null, string.Empty, null), query);
        Assert.Null(vm.SelectedLaunch.Value);
        Assert.Null(vm.SelectedLevel.Value);
    }

    [AvaloniaFact]
    public void The_list_says_it_is_loading_then_that_nothing_matches()
    {
        _source.Hold = true;
        var vm = Started();
        Assert.Equal(Localizer.T("records.loading"), vm.ListNote);

        _source.Pending.Single().SetResult(new RecordsPage([], false));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Localizer.T("records.empty"), vm.ListNote);
        Assert.False(vm.IsListNoteError);
    }

    [AvaloniaFact]
    public void The_empty_note_gives_way_to_the_first_record_and_returns_when_a_filter_matches_none()
    {
        var vm = Started();
        Assert.Equal(Localizer.T("records.empty"), vm.ListNote);

        _source.Add(1);
        vm.OnStored();
        _timers.RunAll();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(vm.Records);
        Assert.Null(vm.ListNote);

        vm.SelectedLevel = vm.LevelOptions.Single(option => option.Value == RecordLevelFilter.Error);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.Records);
        Assert.Equal(Localizer.T("records.empty"), vm.ListNote);
    }

    [AvaloniaFact]
    public void A_failed_first_page_shows_a_note_and_is_logged_without_its_raw_text()
    {
        _source.PageFailure = new IOException("disk I/O error");

        var vm = Started();

        Assert.Equal(RecordsListState.Failed, vm.ListState);
        Assert.Equal(Localizer.T("records.loadFailed"), vm.ListNote);
        Assert.True(vm.IsListNoteError);
        Assert.DoesNotContain("disk", vm.ListNote);
        Assert.Contains(("warn", "Records read failed"), _log.Entries);
    }

    [AvaloniaFact]
    public void A_level_filter_reads_the_first_page_again_for_that_level()
    {
        _source.Add(3);
        _source.Add(2, level: "error");
        var vm = Started();

        vm.SelectedLevel = vm.LevelOptions.Single(option => option.Value == RecordLevelFilter.Attention);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(RecordLevelFilter.Attention, _source.PageQueries[^1].Level);
        Assert.Null(_source.PageQueries[^1].After);
        Assert.Equal([5L, 4L], Ids(vm));
    }

    [AvaloniaFact]
    public void The_level_filter_offers_needs_attention_then_each_level()
    {
        var vm = Create();

        Assert.Equal(
            [null, RecordLevelFilter.Attention, RecordLevelFilter.Error, RecordLevelFilter.Warn, RecordLevelFilter.Info, RecordLevelFilter.Debug],
            vm.LevelOptions.Select(option => option.Value));
        Assert.Equal(Localizer.T("records.allLevels"), vm.LevelOptions[0].Text);
        Assert.Equal(Localizer.T("records.levelAttention"), vm.LevelOptions[1].Text);
    }

    [AvaloniaFact]
    public void The_search_applies_once_after_the_last_keystroke()
    {
        _source.Add(12);
        var vm = Started();

        vm.SearchText = "Mess";
        vm.SearchText = "Message 1";
        Assert.Single(_source.PageQueries);
        Assert.Equal([RecordsWindowViewModel.SearchDelay], _timers.Delays);

        _timers.RunAll();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, _source.PageQueries.Count);
        Assert.Equal("Message 1", _source.PageQueries[^1].Search);
        Assert.Equal([12L, 11L, 10L, 1L], Ids(vm));
    }

    [AvaloniaFact]
    public void A_page_read_for_older_filters_is_dropped()
    {
        _source.Add(4);
        _source.Hold = true;
        var vm = Started();
        vm.SelectedLevel = vm.LevelOptions.Single(option => option.Value == RecordLevelFilter.Error);
        Dispatcher.UIThread.RunJobs();

        _source.Pending[1].SetResult(new RecordsPage([], false));
        _source.Pending[0].SetResult(_source.Answer(new RecordsQuery(null, null, string.Empty, null)));
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(vm.Records);
        Assert.Equal(Localizer.T("records.empty"), vm.ListNote);
    }

    [AvaloniaFact]
    public void The_launch_filter_lists_every_launch_newest_first_and_names_this_one()
    {
        _source.Add(1, session: "2026-10-03T09:00:00.000Z");
        _source.Add(1, session: _source.Session);

        var vm = Started();

        Assert.Equal([null, _source.Session, "2026-10-03T09:00:00.000Z"], vm.LaunchOptions.Select(option => option.Value));
        Assert.Equal(Localizer.T("records.allLaunches"), vm.LaunchOptions[0].Text);
        Assert.Equal(
            Localizer.T("records.thisLaunch", ("time", RecordFormat.TimeText(_source.Session, TimeZoneInfo.Utc))),
            vm.LaunchOptions[1].Text);

        vm.SelectedLaunch = vm.LaunchOptions[2];
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("2026-10-03T09:00:00.000Z", _source.PageQueries[^1].Session);
        Assert.Equal([1L], Ids(vm));
    }

    [AvaloniaFact]
    public void Reaching_the_end_reads_the_next_page_once_from_the_last_record()
    {
        _source.Add(250);
        _source.Hold = false;
        var vm = Started();
        _source.Hold = true;

        vm.ViewportChanged(atTop: false, nearEnd: true, byReader: true);
        vm.ViewportChanged(atTop: false, nearEnd: true, byReader: true);

        Assert.Equal(2, _source.PageQueries.Count);
        Assert.Equal(new RecordCursor(vm.Records[^1].Record.Time, 151), _source.PageQueries[^1].After);
        Assert.True(vm.IsLoadingMore);
        Assert.Equal(Localizer.T("records.loading"), vm.EndNote);

        _source.Pending.Single().SetResult(_source.Answer(_source.PageQueries[^1]));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(200, vm.Records.Count);
        Assert.False(vm.IsLoadingMore);
        Assert.Null(vm.EndNote);
    }

    [AvaloniaFact]
    public void A_failed_next_page_waits_for_the_reader_to_reach_the_end_again()
    {
        _source.Add(150);
        var vm = Started();
        _source.PageFailure = new IOException("busy");

        vm.ViewportChanged(atTop: false, nearEnd: true, byReader: true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.MoreFailed);
        Assert.Equal(Localizer.T("records.loadFailed"), vm.EndNote);

        // A layout pass that leaves the end in view is not the reader reaching it.
        vm.ViewportChanged(atTop: false, nearEnd: true, byReader: false);
        Assert.Equal(2, _source.PageQueries.Count);

        _source.PageFailure = null;
        vm.ViewportChanged(atTop: false, nearEnd: true, byReader: true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, _source.PageQueries.Count);
        Assert.False(vm.MoreFailed);
        Assert.Equal(150, vm.Records.Count);
    }

    [AvaloniaFact]
    public void A_page_that_leaves_the_pane_short_reads_the_next_by_itself()
    {
        _source.Add(150);
        var vm = Started();

        vm.ViewportChanged(atTop: true, nearEnd: true, byReader: false);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(150, vm.Records.Count);
    }

    [AvaloniaFact]
    public void Selecting_the_last_row_reads_the_next_page()
    {
        _source.Add(150);
        var vm = Started();

        vm.SelectedRecord = vm.Records[^1];
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(150, vm.Records.Count);
    }

    [AvaloniaFact]
    public void Selecting_a_record_shows_it_whole()
    {
        _source.Add(3);
        var vm = Started();

        vm.SelectedRecord = vm.Records[1];
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(RecordDetailState.Ready, vm.DetailState);
        Assert.True(vm.HasDetail);
        Assert.Null(vm.DetailNote);
        Assert.Equal("Message 2", vm.Detail!.Title);
        Assert.Contains("\"index\": 2", vm.Detail.Details);
        Assert.Equal(
            [Localizer.T("records.time"), Localizer.T("records.level"), Localizer.T("records.launch")],
            vm.Detail.Fields.Select(field => field.Label));
    }

    [AvaloniaFact]
    public void Nothing_selected_and_a_failed_record_each_say_so()
    {
        _source.Add(1);
        var vm = Started();
        Assert.Equal(Localizer.T("records.noSelection"), vm.DetailNote);

        _source.DetailFailure = new IOException("gone");
        vm.SelectedRecord = vm.Records[0];
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Localizer.T("records.detailFailed"), vm.DetailNote);
        Assert.True(vm.IsDetailNoteError);
    }

    [AvaloniaFact]
    public void A_stored_record_is_read_at_most_once_a_second_while_the_list_is_at_the_top()
    {
        _source.Add(3);
        var vm = Started();
        vm.SelectedRecord = vm.Records[1];
        Dispatcher.UIThread.RunJobs();
        var selected = vm.SelectedRecord;

        _source.Add(2);
        vm.OnStored();
        vm.OnStored();
        Assert.Equal([RecordsWindowViewModel.LiveInterval], _timers.Delays);

        _timers.RunAll();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([5L, 4L, 3L, 2L, 1L], Ids(vm));
        Assert.Same(selected, vm.SelectedRecord);
        Assert.Equal(RecordsListState.Ready, vm.ListState);
    }

    [AvaloniaFact]
    public void Rows_stay_on_screen_while_the_newest_page_is_read_again()
    {
        _source.Add(3);
        var vm = Started();
        _source.Hold = true;

        vm.OnStored();
        _timers.RunAll();

        Assert.Equal(3, vm.Records.Count);
        Assert.Equal(RecordsListState.Ready, vm.ListState);
        Assert.Null(vm.ListNote);
    }

    [AvaloniaFact]
    public void Away_from_the_top_new_records_wait_until_the_list_is_back_there()
    {
        _source.Add(3);
        var vm = Started();
        vm.ViewportChanged(atTop: false, nearEnd: false, byReader: true);

        _source.Add(1);
        vm.OnStored();
        _timers.RunAll();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, vm.Records.Count);

        vm.ViewportChanged(atTop: true, nearEnd: false, byReader: true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([4L, 3L, 2L, 1L], Ids(vm));
    }

    [AvaloniaFact]
    public void After_a_failed_read_signals_are_ignored_until_a_read_succeeds()
    {
        _source.Add(3);
        var vm = Started();

        // The live read fails; its own log line would be the next signal.
        _source.PageFailure = new IOException("locked");
        vm.OnStored();
        _timers.RunAll();
        Dispatcher.UIThread.RunJobs();
        vm.OnStored();
        Assert.Equal(0, _timers.Pending);

        // A read that succeeds, here a filter change, resumes them.
        _source.PageFailure = null;
        vm.SelectedLevel = vm.LevelOptions.Single(option => option.Value == RecordLevelFilter.Info);
        Dispatcher.UIThread.RunJobs();
        vm.OnStored();
        Assert.Equal(1, _timers.Pending);
    }

    [AvaloniaFact]
    public void The_source_signal_reaches_the_list_on_the_ui_thread_and_stops_when_disposed()
    {
        _source.Add(1);
        var vm = Started();
        Assert.Equal(1, _source.Subscribers);

        _source.RaiseStored();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, _timers.Pending);

        vm.Dispose();
        Assert.Equal(0, _source.Subscribers);
        Assert.Equal(0, _timers.Pending);
    }

    [AvaloniaFact]
    public void Reading_the_records_writes_none()
    {
        _source.Add(150);
        var vm = Started();
        vm.ViewportChanged(atTop: false, nearEnd: true, byReader: true);
        vm.SelectedRecord = vm.Records[0];
        vm.OnStored();
        _timers.RunAll();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(_log.Entries);
    }
}
