using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Core.Identity;
using DayNote.Logging;
using DayNote.Tests.ViewModels;
using DayNote.ViewModels;
using DayNote.Views;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The records window's paging and live updates in the real window, over a real records database read
/// through the logger that owns it, judged by the scroll offset, visible height and content height the
/// list's own scroll viewer reports (logging-conventions, The Records window). The live interval is the
/// test's to end.
/// </summary>
public sealed class RecordsWindowScrollTests : WindowTest, IDisposable
{
    private const int Seeded = 10_000;
    private const string SeedSession = "2026-01-01T00:00:00.000Z";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "daynote-records-scroll", IdGenerator.New());
    private readonly ManualScheduler _timers = new();
    private readonly RecordsLogger _log;

    public RecordsWindowScrollTests()
    {
        Directory.CreateDirectory(_root);
        // Opening creates the schema the seeded rows go into.
        _log = RecordsLogger.Open(RecordsFile, Path.Combine(_root, "logs"), debugEnabled: false);
    }

    private string RecordsFile => Path.Combine(_root, "records.sqlite3");

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = RecordsFile, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private void Seed()
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO logs (time, session, level, message, note_id, fields) VALUES ($t, $s, 'info', $m, NULL, '{}')";
        var time = command.Parameters.Add("$t", SqliteType.Text);
        command.Parameters.AddWithValue("$s", SeedSession);
        var message = command.Parameters.Add("$m", SqliteType.Text);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < Seeded; index++)
        {
            time.Value = start.AddSeconds(index).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            message.Value = $"seeded {index}";
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    // Runs the window until what holds holds, waking on each change the list or its view model makes.
    private static void Until(Window window, RecordsWindowViewModel records, Func<bool> holds)
    {
        Settle(window);
        while (!holds())
        {
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnRows(object? sender, NotifyCollectionChangedEventArgs e) => changed.TrySetResult();
            void OnProperty(object? sender, PropertyChangedEventArgs e) => changed.TrySetResult();
            records.Records.CollectionChanged += OnRows;
            records.PropertyChanged += OnProperty;
            try
            {
                if (!holds())
                    RunUntil(changed.Task.WaitAsync(TestContext.Current.CancellationToken));
            }
            finally
            {
                records.Records.CollectionChanged -= OnRows;
                records.PropertyChanged -= OnProperty;
            }

            Settle(window);
        }
    }

    // Logs a record, waits until the database has stored it and the window has heard of it, and ends
    // the live interval the window then waits out.
    private void Arrive(Window window, string message)
    {
        using var stored = new ManualResetEventSlim();
        void Heard() => stored.Set();
        _log.Stored += Heard;
        try
        {
            _log.Info(message);
            stored.Wait(TestContext.Current.CancellationToken);
        }
        finally
        {
            _log.Stored -= Heard;
        }

        Settle(window);
        _timers.RunAll();
        Settle(window);
    }

    // The first row whose container shows inside the viewport, and how far its top sits from the viewport's.
    private static (long Id, double Top) FirstVisible(ListBox list, ScrollViewer scroll) =>
        list.GetRealizedContainers()
            .Select(container => (container, top: container.TranslatePoint(default, scroll)!.Value.Y))
            .Where(item => item.top + item.container.Bounds.Height > 0)
            .OrderBy(item => item.top)
            .Select(item => (((RecordRowViewModel)item.container.DataContext!).Record.Id, item.top))
            .First();

    private static double Remaining(ScrollViewer scroll) =>
        scroll.Extent.Height - scroll.Offset.Y - scroll.Viewport.Height;

    [AvaloniaFact]
    public void The_list_pages_through_every_record_and_new_ones_appear_only_at_its_top()
    {
        Seed();
        var records = new RecordsWindowViewModel(_log, new FakeRecordsHost(), new RecordingLogger(), _timers.Schedule);
        var window = Show(new RecordsWindow(records));
        var list = window.FindControl<ListBox>("RecordsList")!;
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        Until(window, records, () => records.ListState == RecordsListState.Ready && !records.IsLoadingMore);

        // The first page: the 100 newest, newest first, filling more than the window.
        Assert.Equal(RecordsReads.PageSize, records.Records.Count);
        Assert.Equal(Enumerable.Range(Seeded - RecordsReads.PageSize, RecordsReads.PageSize).Reverse().Select(i => $"seeded {i}"),
            records.Records.Select(row => row.Record.Message));
        Assert.True(Remaining(scroll) > scroll.Viewport.Height);

        // At the top, a new record appears at the top.
        Arrive(window, "live 1");
        Until(window, records, () => records.Records[0].Record.Message == "live 1");
        Assert.True(scroll.Offset.Y < 1);

        // Scrolled down, with a record selected: arrivals move nothing, then join at the top.
        scroll.Offset = new Vector(0, scroll.Viewport.Height * 2);
        Until(window, records, () => !records.IsLoadingMore);
        var shownId = FirstVisible(list, scroll).Id;
        var selected = records.Records.Single(row => row.Record.Id == shownId);
        records.SelectedRecord = selected;
        Until(window, records, () => records.HasDetail);
        var offset = scroll.Offset;
        var first = FirstVisible(list, scroll);
        for (var arrival = 2; arrival <= 4; arrival++)
        {
            Arrive(window, $"live {arrival}");
            Assert.Equal(offset, scroll.Offset);
            Assert.Equal(first, FirstVisible(list, scroll));
            Assert.Equal("live 1", records.Records[0].Record.Message);
            Assert.Same(selected, records.SelectedRecord);
            Assert.Equal(selected.Record.Id, records.Detail!.Record.Id);
        }

        scroll.Offset = default;
        Until(window, records, () => records.Records[0].Record.Message == "live 4");
        Assert.Equal(["live 4", "live 3", "live 2", "live 1"], records.Records.Take(4).Select(row => row.Record.Message));
        Assert.Same(selected, records.SelectedRecord);

        // Just over a screen from the end reads nothing; within one screen reads the next page, through
        // every record, each page joining in one step. The list estimates the height of rows it has not
        // laid out yet, so its reported extent can change as a jump lays out new rows; a page read for an
        // earlier estimate is let finish, and the position taken again, until the list holds still there.
        var settled = -1;
        while (settled != records.Records.Count || records.IsLoadingMore)
        {
            Until(window, records, () => !records.IsLoadingMore);
            settled = records.Records.Count;
            for (var pass = 0; pass < 4 && Math.Abs(Remaining(scroll) - scroll.Viewport.Height - 1) > 0.5; pass++)
            {
                scroll.Offset = new Vector(0, scroll.Extent.Height - 2 * scroll.Viewport.Height - 1);
                Settle(window);
            }
        }

        Settle(window);
        Assert.True(Remaining(scroll) > scroll.Viewport.Height);
        Assert.False(records.IsLoadingMore);
        Assert.Equal(settled, records.Records.Count);
        var shown = records.Records.Count;
        while (shown < Seeded + 4)
        {
            scroll.Offset = new Vector(0, scroll.Extent.Height - 2 * scroll.Viewport.Height + 1);
            Until(window, records, () => records.Records.Count > shown && !records.IsLoadingMore);
            Assert.Equal(Math.Min(shown + RecordsReads.PageSize, Seeded + 4), records.Records.Count);
            shown = records.Records.Count;
        }

        window.Close();
        Dispatcher.UIThread.RunJobs();

        var stored = new List<long>();
        using (var connection = Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM logs ORDER BY time DESC, id DESC";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                stored.Add(reader.GetInt64(0));
        }

        Assert.Equal(stored, records.Records.Select(row => row.Record.Id));
    }

    void IDisposable.Dispose()
    {
        Dispose();
        _log.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; a leftover temp folder must not fail the test.
        }
    }
}
