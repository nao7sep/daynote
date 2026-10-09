using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using DayNote.Logging;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DayNote.Tests.Logging;

public sealed class RecordsLoggerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "daynote-recordstests", IdGenerator.New());

    public RecordsLoggerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
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
    private string LogsDirectory => Path.Combine(_root, "logs");

    private RecordsLogger Open(bool debugEnabled = false) =>
        RecordsLogger.Open(RecordsFile, LogsDirectory, debugEnabled);

    private sealed record Row(string Time, string Session, string Level, string Message, string? NoteId, JsonObject Fields);

    private List<Row> ReadRows()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = RecordsFile,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT time, session, level, message, note_id, fields FROM logs ORDER BY id";
        using var reader = command.ExecuteReader();
        var rows = new List<Row>();
        while (reader.Read())
        {
            rows.Add(new Row(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                JsonNode.Parse(reader.GetString(5))!.AsObject()));
        }

        return rows;
    }

    private const string IsoPattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$";

    [Fact]
    public void Writes_each_entry_as_a_row_with_its_time_session_and_fields()
    {
        using (var log = Open())
        {
            log.Info("Binder opened", new { path = "/tmp/x.daynote", noteCount = 3 });
        }

        var row = Assert.Single(ReadRows());
        Assert.Matches(IsoPattern, row.Time);
        Assert.Matches(IsoPattern, row.Session);
        Assert.Equal("info", row.Level);
        Assert.Equal("Binder opened", row.Message);
        Assert.Null(row.NoteId);
        Assert.Equal("/tmp/x.daynote", (string?)row.Fields["path"]);
        Assert.Equal(3, (int)row.Fields["noteCount"]!);
    }

    [Fact]
    public void Keys_an_entry_that_names_a_note_by_its_id()
    {
        using (var log = Open())
        {
            log.Info("Created note", new { noteId = "abc123" });
        }

        var row = Assert.Single(ReadRows());
        Assert.Equal("abc123", row.NoteId);
        Assert.Equal("abc123", (string?)row.Fields["noteId"]);
    }

    [Fact]
    public void One_launch_is_one_session_and_the_next_launch_another()
    {
        using (var first = Open())
        {
            first.Info("a");
            first.Info("b");
        }

        System.Threading.Thread.Sleep(5);

        using (var second = Open())
        {
            second.Info("c");
        }

        var sessions = ReadRows().Select(r => r.Session).ToArray();
        Assert.Equal(3, sessions.Length);
        Assert.Equal(sessions[0], sessions[1]);
        Assert.NotEqual(sessions[1], sessions[2]);
    }

    [Fact]
    public void Captures_the_exception_type_message_stack_and_cause_chain()
    {
        Exception captured;
        try
        {
            try
            {
                throw new IOException("disk gone");
            }
            catch (Exception inner)
            {
                throw new InvalidOperationException("save failed", inner);
            }
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        using (var log = Open())
        {
            log.Error("Failed to save binder", new { path = "/tmp/x" }, captured);
        }

        var error = Assert.Single(ReadRows()).Fields["error"]!;
        Assert.Equal(typeof(InvalidOperationException).FullName, (string?)error["type"]);
        Assert.Equal("save failed", (string?)error["message"]);
        Assert.False(string.IsNullOrEmpty((string?)error["stack"]));

        var cause = error["cause"]!;
        Assert.Equal(typeof(IOException).FullName, (string?)cause["type"]);
        Assert.Equal("disk gone", (string?)cause["message"]);
    }

    [Fact]
    public void Fans_an_aggregate_exception_into_a_causes_array_keeping_every_inner()
    {
        var aggregate = new AggregateException(
            new IOException("disk one"),
            new InvalidOperationException("bad state two"),
            new TimeoutException("slow three"));

        using (var log = Open())
        {
            log.Error("Unobserved task exception", error: aggregate);
        }

        var error = Assert.Single(ReadRows()).Fields["error"]!;
        Assert.Equal(typeof(AggregateException).FullName, (string?)error["type"]);
        Assert.Null(error["cause"]);

        var causes = error["causes"]!.AsArray();
        Assert.Equal(3, causes.Count);
        Assert.Equal("disk one", (string?)causes[0]!["message"]);
        Assert.Equal("bad state two", (string?)causes[1]!["message"]);
        Assert.Equal("slow three", (string?)causes[2]!["message"]);
    }

    [Fact]
    public void Debug_is_suppressed_when_disabled_and_written_when_enabled()
    {
        using (var log = Open(debugEnabled: false))
        {
            log.Debug("diagnostic");
            log.Info("normal");
        }

        Assert.Equal(["info"], ReadRows().Select(r => r.Level));

        using (var log = Open(debugEnabled: true))
        {
            log.Debug("diagnostic");
        }

        Assert.Equal(["info", "debug"], ReadRows().Select(r => r.Level));
    }

    [Fact]
    public void An_entry_is_readable_while_the_session_is_still_open()
    {
        using var log = Open();
        log.Info("now");
        log.Flush();

        Assert.Equal("now", Assert.Single(ReadRows()).Message);
    }

    [Fact]
    public void Logging_returns_while_the_database_is_locked_and_the_entry_is_written_once_it_is_free()
    {
        using (var log = Open())
        {
            log.Info("before");
            log.Flush();

            using var holder = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = RecordsFile,
                Pooling = false,
            }.ToString());
            holder.Open();
            using (holder.BeginTransaction())
            {
                var stopwatch = Stopwatch.StartNew();
                log.Info("while locked");
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Logging waited {stopwatch.Elapsed}.");
            }
        }

        Assert.Equal(["before", "while locked"], ReadRows().Select(r => r.Message));
    }

    [Theory]
    [InlineData(42)]
    [InlineData("a bare string")]
    public void Keeps_a_non_object_data_value_under_a_data_field(object scalar)
    {
        using (var log = Open())
        {
            log.Info("odd payload", scalar);
        }

        Assert.Equal(scalar.ToString(), Assert.Single(ReadRows()).Fields["data"]!.ToString());
    }

    [Fact]
    public void Keeps_the_entry_with_the_render_error_when_the_data_object_cannot_serialize()
    {
        using (var log = Open())
        {
            log.Info("event survived", new ExplodingData());
        }

        var row = Assert.Single(ReadRows());
        Assert.Equal("event survived", row.Message);
        Assert.False(string.IsNullOrEmpty((string?)row.Fields["logError"]));
    }

    [Fact]
    public void Never_throws_and_still_writes_a_row_for_a_pathological_message()
    {
        var ex = Record.Exception(() =>
        {
            using var log = Open();
            log.Info("\uD800");
        });

        Assert.Null(ex);
        Assert.Equal("info", Assert.Single(ReadRows()).Level);
    }

    [Fact]
    public void Falls_back_to_a_session_file_under_logs_when_the_database_cannot_open()
    {
        // A directory sits where the database file should be, so opening it fails.
        Directory.CreateDirectory(RecordsFile);

        var ex = Record.Exception(() =>
        {
            using var log = Open(debugEnabled: true);
            log.Info("first", new { noteId = "n1" });
            log.Error("second", error: new InvalidOperationException("boom"));
        });

        Assert.Null(ex);
        var file = Assert.Single(Directory.GetFiles(LogsDirectory));
        Assert.Matches(@"^\d{8}-\d{6}-\d{3}-utc\.log$", Path.GetFileName(file));

        var lines = File.ReadAllLines(file).Select(line => JsonNode.Parse(line)!).ToArray();
        Assert.Equal(3, lines.Length);
        // The session says once, first, why its records are here.
        Assert.Equal("warn", (string?)lines[0]["level"]);
        Assert.StartsWith("Records database could not be opened", (string?)lines[0]["message"]);
        Assert.Equal("first", (string?)lines[1]["message"]);
        Assert.Equal("n1", (string?)lines[1]["fields"]!["noteId"]);
        Assert.Equal((string?)lines[1]["session"], (string?)lines[2]["session"]);
        Assert.Equal("boom", (string?)lines[2]["fields"]!["error"]!["message"]);
        Assert.False(string.IsNullOrEmpty((string?)lines[2]["recordsError"]));
    }

    // ----- Format version (PRAGMA user_version) --------------------------------------------------

    private long UserVersion()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = RecordsFile,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)command.ExecuteScalar()!;
    }

    private void CreateDatabase(long userVersion)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = RecordsFile,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE logs (id INTEGER PRIMARY KEY); PRAGMA user_version = {userVersion};";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void A_new_database_records_the_current_format_version()
    {
        using (var log = Open())
        {
            log.Info("first");
        }

        Assert.Equal(FormatVersions.Records, UserVersion());
        Assert.Single(ReadRows());
    }

    [Theory]
    [InlineData(0)] // tables but no version: unreadable
    [InlineData(-1)] // a version no DayNote writes
    [InlineData(FormatVersions.Records + 1)]
    public void An_unversioned_or_newer_database_is_left_byte_identical_and_the_session_falls_back_with_one_warning(long userVersion)
    {
        CreateDatabase(userVersion);
        var before = File.ReadAllBytes(RecordsFile);

        using (var log = Open())
        {
            log.Info("kept elsewhere");
        }

        Assert.Equal(before, File.ReadAllBytes(RecordsFile));
        Assert.Equal(new[] { "records.sqlite3" }, Directory.GetFiles(_root).Select(Path.GetFileName));
        var lines = File.ReadAllLines(Assert.Single(Directory.GetFiles(LogsDirectory))).Select(line => JsonNode.Parse(line)!).ToArray();
        Assert.Single(lines, line => (string?)line["level"] == "warn");
        Assert.Equal("kept elsewhere", (string?)lines[1]["message"]);
    }

    [Fact]
    public void Never_throws_when_neither_the_database_nor_the_fallback_file_can_be_written()
    {
        Directory.CreateDirectory(RecordsFile);
        // A file sits where the fallback directory should be.
        File.WriteAllText(LogsDirectory, "occupied");

        var ex = Record.Exception(() =>
        {
            using var log = Open(debugEnabled: true);
            log.Info("info");
            log.Warn("warn");
            log.Error("error", error: new InvalidOperationException("boom"));
            log.Debug("debug");
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Does_not_create_the_fallback_directory_while_the_database_works()
    {
        using (var log = Open())
        {
            log.Warn("fine");
        }

        Assert.False(Directory.Exists(LogsDirectory));
    }

    /// <summary>A data object whose serialization always throws, to exercise the render fallback.</summary>
    private sealed class ExplodingData
    {
        public string Boom => throw new InvalidOperationException("getter exploded");
    }
}
