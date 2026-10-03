using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DayNote.Core.Identity;
using DayNote.Logging;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DayNote.Tests.Logging;

/// <summary>
/// The records window's reads, through the logger that owns the database: pages newest first,
/// keyset paged, filtered by launch, level and search; one record whole; the launches; and the
/// signal after each stored entry.
/// </summary>
public sealed class RecordsReadsTests : IDisposable
{
    private const string Earlier = "2026-10-03T09:00:00.000Z";
    private const string Later = "2026-10-04T09:00:00.000Z";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "daynote-recordsreads", IdGenerator.New());
    private readonly RecordsLogger _log;

    public RecordsReadsTests()
    {
        Directory.CreateDirectory(_root);
        // Opening creates the schema the rows below are put into.
        _log = RecordsLogger.Open(RecordsFile, Path.Combine(_root, "logs"), debugEnabled: true);
    }

    public void Dispose()
    {
        _log.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort; a leftover temp dir must not fail the test.
        }
    }

    private string RecordsFile => Path.Combine(_root, "records.sqlite3");

    // Rows put in directly, with the times, levels and sessions a test needs.
    private void Insert(params (string Time, string Session, string Level, string Message, string? NoteId, string Fields)[] rows)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = RecordsFile, Pooling = false }.ToString());
        connection.Open();
        foreach (var row in rows)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO logs (time, session, level, message, note_id, fields) VALUES ($t, $s, $l, $m, $n, $f)";
            command.Parameters.AddWithValue("$t", row.Time);
            command.Parameters.AddWithValue("$s", row.Session);
            command.Parameters.AddWithValue("$l", row.Level);
            command.Parameters.AddWithValue("$m", row.Message);
            command.Parameters.AddWithValue("$n", (object?)row.NoteId ?? DBNull.Value);
            command.Parameters.AddWithValue("$f", row.Fields);
            command.ExecuteNonQuery();
        }
    }

    private long Count()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = RecordsFile, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM logs";
        return (long)command.ExecuteScalar()!;
    }

    private static string At(int second) => $"2026-10-04T10:{second / 60:00}:{second % 60:00}.000Z";

    private static RecordsQuery All(RecordCursor? after = null) => new(null, null, string.Empty, after);

    [Fact]
    public async Task Pages_hold_a_hundred_records_newest_first_and_follow_on_without_gaps()
    {
        // Three records share each second, so the cursor has to break ties by id.
        var rows = Enumerable.Range(0, 250)
            .Select(index => (At(index / 3), Later, "info", $"m{index}", (string?)null, "{}"))
            .ToArray();
        Insert(rows);

        var seen = new List<RecordSummary>();
        RecordCursor? after = null;
        var pages = 0;
        while (true)
        {
            var page = await _log.ReadPageAsync(All(after));
            pages++;
            seen.AddRange(page.Records);
            if (!page.More)
                break;
            Assert.Equal(RecordsReads.PageSize, page.Records.Count);
            after = new RecordCursor(page.Records[^1].Time, page.Records[^1].Id);
        }

        Assert.Equal(3, pages);
        // Every record once, in the order the list shows.
        Assert.Equal(250, seen.Count);
        Assert.Equal(seen.Count, seen.Select(record => record.Id).Distinct().Count());
        var ordered = seen.ToList();
        ordered.Sort(DayNote.ViewModels.RecordsPaging.NewestFirst);
        Assert.Equal(ordered, seen);
    }

    [Fact]
    public async Task Needs_attention_is_warnings_and_errors_and_a_level_is_that_level_alone()
    {
        Insert(
            (At(1), Later, "debug", "d", null, "{}"),
            (At(2), Later, "info", "i", null, "{}"),
            (At(3), Later, "warn", "w", null, "{}"),
            (At(4), Later, "error", "e", null, "{}"));

        var attention = await _log.ReadPageAsync(new RecordsQuery(null, RecordLevelFilter.Attention, "", null));
        var errors = await _log.ReadPageAsync(new RecordsQuery(null, RecordLevelFilter.Error, "", null));
        var debug = await _log.ReadPageAsync(new RecordsQuery(null, RecordLevelFilter.Debug, "", null));

        Assert.Equal(["e", "w"], attention.Records.Select(record => record.Message));
        Assert.Equal(["e"], errors.Records.Select(record => record.Message));
        Assert.Equal(["d"], debug.Records.Select(record => record.Message));
    }

    [Fact]
    public async Task A_launch_filter_reads_that_session_alone_and_the_launches_come_newest_first()
    {
        Insert(
            (At(1), Earlier, "info", "before", null, "{}"),
            (At(2), Later, "info", "after", null, "{}"));

        var page = await _log.ReadPageAsync(new RecordsQuery(Earlier, null, "", null));
        var sessions = await _log.ReadSessionsAsync();

        Assert.Equal(["before"], page.Records.Select(record => record.Message));
        Assert.Equal([Later, Earlier], sessions);
    }

    [Fact]
    public async Task The_search_looks_in_the_message_the_note_and_the_fields_and_takes_wildcards_literally()
    {
        Insert(
            (At(1), Later, "info", "Binder saved", null, """{"path":"/notes/100%.daynote"}"""),
            (At(2), Later, "info", "Note opened", "note_7", "{}"),
            (At(3), Later, "info", "Something else", null, """{"path":"/notes/1000.daynote"}"""));

        async Task<string[]> Search(string text) =>
            (await _log.ReadPageAsync(new RecordsQuery(null, null, text, null))).Records.Select(record => record.Message).ToArray();

        Assert.Equal(["Binder saved"], await Search("binder"));
        Assert.Equal(["Note opened"], await Search("note_7"));
        Assert.Equal(["Binder saved"], await Search("100%"));
        Assert.Equal(["Note opened"], await Search("e_"));
        Assert.Equal(3, (await Search("  ")).Length);
    }

    [Fact]
    public async Task A_record_reads_whole_and_a_missing_one_reads_as_none()
    {
        Insert((At(5), Later, "warn", "Binder missing", "n1", """{"path":"/x.daynote","noteId":"n1"}"""));
        var id = (await _log.ReadPageAsync(All())).Records.First(record => record.Message == "Binder missing").Id;

        var record = await _log.ReadDetailAsync(id);

        Assert.Equal(new RecordDetail(id, At(5), Later, "warn", "Binder missing", "n1", """{"path":"/x.daynote","noteId":"n1"}"""), record);
        Assert.Null(await _log.ReadDetailAsync(id + 1000));
    }

    [Fact]
    public async Task Reading_writes_no_record()
    {
        Insert((At(1), Later, "info", "one", null, "{}"));
        var before = Count();

        await _log.ReadPageAsync(All());
        await _log.ReadSessionsAsync();
        await _log.ReadDetailAsync(1);
        _log.Flush();

        Assert.Equal(before, Count());
    }

    [Fact]
    public async Task A_read_sees_every_entry_logged_before_it()
    {
        _log.Info("just now");

        var page = await _log.ReadPageAsync(All());

        Assert.Equal("just now", page.Records[0].Message);
    }

    [Fact]
    public async Task Each_stored_entry_signals_once_and_a_read_signals_nothing()
    {
        var signals = 0;
        _log.Stored += () => System.Threading.Interlocked.Increment(ref signals);

        _log.Info("one");
        _log.Warn("two");
        await _log.ReadPageAsync(All());
        _log.Flush();

        Assert.Equal(2, signals);
    }

    [Fact]
    public async Task An_entry_that_went_to_the_fallback_file_signals_nothing_and_reads_fail()
    {
        var root = Path.Combine(_root, "unopenable");
        Directory.CreateDirectory(Path.Combine(root, "records.sqlite3"));
        using var log = RecordsLogger.Open(Path.Combine(root, "records.sqlite3"), Path.Combine(root, "logs"), debugEnabled: false);
        var signals = 0;
        log.Stored += () => signals++;

        log.Info("to the file");
        log.Flush();

        Assert.Equal(0, signals);
        await Assert.ThrowsAsync<InvalidOperationException>(() => log.ReadPageAsync(All()));
    }

    [Fact]
    public async Task A_read_after_the_logger_closed_fails_instead_of_waiting()
    {
        _log.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _log.ReadSessionsAsync());
    }
}
