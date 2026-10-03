using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayNote.Core.Time;
using Microsoft.Data.Sqlite;

namespace DayNote.Logging;

/// <summary>
/// DayNote's only logger: each entry is a row in <c>records.sqlite3</c>, per the logging and
/// data-lifecycle conventions. Hand-rolled on <see cref="System.Text.Json"/> + one SQLite connection (no
/// logging framework). This process is the database's only writer, and one thread of it owns the
/// connection and writes every entry in the order it was logged, so no caller waits on the disk.
/// </summary>
/// <remarks>
/// A row holds the event <c>time</c>, its <c>session</c> (this launch's start time), <c>level</c>,
/// <c>message</c>, the <c>note_id</c> it concerns when the entry names a <c>noteId</c>, and every other
/// field given as one JSON object. An entry the database cannot take is appended as one JSON line to
/// <c>logs/yyyymmdd-hhmmss-fff-utc.log</c>, named for the session, carrying the database's error; if that
/// fails too, it goes to <see cref="Console.Error"/>. Logging never throws.
///
/// The records window reads through the same thread (<see cref="IRecordsSource"/>), so a read sees
/// every entry logged before it and never contends with a write for the connection.
/// </remarks>
public sealed class RecordsLogger : IAppLogger, IRecordsSource, IDisposable
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS logs (
          id      INTEGER PRIMARY KEY,
          time    TEXT NOT NULL,
          session TEXT NOT NULL,
          level   TEXT NOT NULL,
          message TEXT NOT NULL,
          note_id TEXT,
          fields  TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_logs_session ON logs (session, id);
        CREATE INDEX IF NOT EXISTS idx_logs_note_id ON logs (note_id, id) WHERE note_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_logs_time ON logs (time, id);
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Keep non-ASCII (file paths, Japanese titles) readable rather than \u-escaped.
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly TimeSpan FlushWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReadWait = TimeSpan.FromSeconds(10);

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _owner;
    private readonly SqliteConnection? _connection;
    private readonly Exception? _openError;
    private readonly string _session;
    private readonly string _fallbackFile;
    private readonly bool _debugEnabled;
    private int _disposed;

    private RecordsLogger(
        SqliteConnection? connection, Exception? openError, DateTimeOffset sessionStart, string fallbackDirectory, bool debugEnabled)
    {
        _connection = connection;
        _openError = openError;
        _session = DayNoteTime.ToIso(sessionStart);
        _fallbackFile = Path.Combine(fallbackDirectory, $"{DayNoteTime.FileStamp(sessionStart)}.log");
        _debugEnabled = debugEnabled;
        _owner = new Thread(Own) { IsBackground = true, Name = "records" };
        _owner.Start();
    }

    /// <summary>
    /// Starts this launch's session against <paramref name="recordsFile"/> (created if missing). If the
    /// database cannot be opened, every entry of the session goes to the fallback file instead.
    /// </summary>
    /// <param name="recordsFile">The app's <c>records.sqlite3</c>.</param>
    /// <param name="fallbackDirectory">The app's <c>logs/</c> directory, created on the first fallback.</param>
    /// <param name="debugEnabled">Whether <see cref="Debug"/> entries are written (off on end-user machines).</param>
    public static RecordsLogger Open(string recordsFile, string fallbackDirectory, bool debugEnabled)
    {
        var sessionStart = DateTimeOffset.UtcNow;
        try
        {
            return new RecordsLogger(OpenDatabase(recordsFile), null, sessionStart, fallbackDirectory, debugEnabled);
        }
        catch (Exception ex)
        {
            return new RecordsLogger(null, ex, sessionStart, fallbackDirectory, debugEnabled);
        }
    }

    private static SqliteConnection OpenDatabase(string recordsFile)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = recordsFile,
            // Disposing the logger releases the file.
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // WAL with synchronous=NORMAL keeps every committed row through an app crash.
            command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;" + Schema;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Debug(string message, object? data = null, Exception? error = null)
    {
        if (_debugEnabled)
        {
            Write("debug", message, data, error);
        }
    }

    public void Info(string message, object? data = null, Exception? error = null) =>
        Write("info", message, data, error);

    public void Warn(string message, object? data = null, Exception? error = null) =>
        Write("warn", message, data, error);

    public void Error(string message, object? data = null, Exception? error = null) =>
        Write("error", message, data, error);

    /// <summary>
    /// Renders the entry now, so it carries the values as they were when logged, and queues its write.
    /// </summary>
    private void Write(string level, string message, object? data, Exception? error)
    {
        var time = DayNoteTime.ToIso(DateTimeOffset.UtcNow);
        var (fields, noteId) = SafeFields(data, error);
        TryQueue(() => Insert(time, level, message, noteId, fields));
    }

    private void Insert(string time, string level, string message, string? noteId, string fields)
    {
        if (_connection is null)
        {
            FallBack(time, level, message, fields, _openError!);
            return;
        }

        try
        {
            using var insert = _connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO logs (time, session, level, message, note_id, fields) " +
                "VALUES ($time, $session, $level, $message, $noteId, $fields)";
            insert.Parameters.AddWithValue("$time", time);
            insert.Parameters.AddWithValue("$session", _session);
            insert.Parameters.AddWithValue("$level", level);
            insert.Parameters.AddWithValue("$message", message);
            insert.Parameters.AddWithValue("$noteId", (object?)noteId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$fields", fields);
            insert.ExecuteNonQuery();
        }
        catch (Exception writeError)
        {
            FallBack(time, level, message, fields, writeError);
            return;
        }

        try
        {
            Stored?.Invoke();
        }
        catch
        {
            // A listener's failure is its own; the entry is stored and the thread keeps writing.
        }
    }

    public string Session => _session;

    public event Action? Stored;

    public Task<RecordsPage> ReadPageAsync(RecordsQuery query) =>
        ReadAsync(connection => RecordsReads.Page(connection, query));

    public Task<RecordDetail?> ReadDetailAsync(long id) =>
        ReadAsync(connection => RecordsReads.Detail(connection, id));

    public Task<IReadOnlyList<string>> ReadSessionsAsync() =>
        ReadAsync(RecordsReads.Sessions);

    /// <summary>
    /// Runs <paramref name="read"/> on the owning thread after every entry queued before it, and
    /// gives up waiting after <see cref="ReadWait"/>. A read logs nothing: each record it wrote would
    /// signal the next read of an open records window.
    /// </summary>
    private Task<T> ReadAsync<T>(Func<SqliteConnection, T> read)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = TryQueue(() =>
        {
            try
            {
                result.TrySetResult(read(_connection ?? throw new InvalidOperationException(
                    "The records database could not be opened.", _openError)));
            }
            catch (Exception ex)
            {
                result.TrySetException(ex);
            }
        });
        if (!queued)
        {
            result.TrySetException(new InvalidOperationException("The records logger is closed."));
        }

        return result.Task.WaitAsync(ReadWait);
    }

    private void Own()
    {
        foreach (var operation in _queue.GetConsumingEnumerable())
        {
            operation();
        }
    }

    /// <summary>Queues <paramref name="operation"/> for the owning thread; false once the logger is closed.</summary>
    private bool TryQueue(Action operation)
    {
        try
        {
            return _queue.TryAdd(operation);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Waits, within a bound, until every entry logged so far is written.</summary>
    public void Flush()
    {
        if (Thread.CurrentThread == _owner)
        {
            return;
        }

        // Never disposed: if the wait times out, the owning thread still sets it later.
        var drained = new ManualResetEventSlim();
        if (TryQueue(drained.Set))
        {
            drained.Wait(FlushWait);
        }
    }

    /// <summary>
    /// Renders the entry's fields without ever throwing: every field given, else the render error alone
    /// (a data object whose serialization throws), else an empty object.
    /// </summary>
    private static (string Fields, string? NoteId) SafeFields(object? data, Exception? error)
    {
        try
        {
            var fields = RenderFields(data, error);
            var noteId = fields["noteId"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;
            return (fields.ToJsonString(SerializerOptions), noteId);
        }
        catch (Exception renderError)
        {
            try
            {
                return (new JsonObject { ["logError"] = renderError.Message }.ToJsonString(SerializerOptions), null);
            }
            catch
            {
                return ("{}", null);
            }
        }
    }

    private static JsonObject RenderFields(object? data, Exception? error)
    {
        var fields = new JsonObject();

        if (data is not null)
        {
            var serialized = JsonSerializer.SerializeToNode(data, SerializerOptions);
            if (serialized is JsonObject given)
            {
                foreach (var field in given.ToArray())
                {
                    // DeepClone re-parents the value cleanly into the new tree.
                    fields[field.Key] = field.Value?.DeepClone();
                }
            }
            else if (serialized is not null)
            {
                // A non-object data value (a scalar or array) is kept under a single field.
                fields["data"] = serialized;
            }
        }

        if (error is not null)
        {
            fields["error"] = BuildError(error);
        }

        return fields;
    }

    /// <summary>
    /// Full exception fidelity: type, message, stack, and the cause chain. An
    /// <see cref="AggregateException"/> fans its multiple inner exceptions out into a <c>causes</c>
    /// array; an ordinary wrapped exception nests its single <c>cause</c>.
    /// </summary>
    private static JsonObject BuildError(Exception error)
    {
        var node = new JsonObject
        {
            ["type"] = error.GetType().FullName,
            ["message"] = error.Message,
            ["stack"] = error.StackTrace,
        };

        if (error is AggregateException aggregate)
        {
            var causes = new JsonArray();
            foreach (var inner in aggregate.InnerExceptions)
            {
                causes.Add(BuildError(inner));
            }

            node["causes"] = causes;
        }
        else if (error.InnerException is { } cause)
        {
            node["cause"] = BuildError(cause);
        }

        return node;
    }

    /// <summary>
    /// Appends the entry the database could not take to this session's fallback file, with the
    /// database's error; failing that, writes both to <see cref="Console.Error"/>. Never throws.
    /// </summary>
    private void FallBack(string time, string level, string message, string fields, Exception recordsError)
    {
        var line = FallbackLine(time, level, message, fields, recordsError);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_fallbackFile)!);
            File.AppendAllText(_fallbackFile, line + "\n");
        }
        catch (Exception fileError)
        {
            try
            {
                Console.Error.WriteLine($"[daynote] log fallback file failed: {fileError.Message}");
                Console.Error.WriteLine(line);
            }
            catch
            {
                // Nothing left to try; logging must not crash the app.
            }
        }
    }

    private string FallbackLine(string time, string level, string message, string fields, Exception recordsError)
    {
        try
        {
            var line = new JsonObject
            {
                ["time"] = time,
                ["session"] = _session,
                ["level"] = level,
                ["message"] = message,
                ["fields"] = JsonNode.Parse(fields),
                ["recordsError"] = recordsError.Message,
            };
            return line.ToJsonString(SerializerOptions);
        }
        catch
        {
            // `time`, `session` and `level` are logger-owned values that need no escaping; the
            // caller's message may be what failed to serialize, so it is left out.
            return $"{{\"time\":\"{time}\",\"session\":\"{_session}\",\"level\":\"{level}\"," +
                   "\"message\":\"[log entry could not be rendered]\"}";
        }
    }

    /// <summary>
    /// Writes what is queued, within a bound, and closes the database. Entries logged afterwards are
    /// dropped.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _queue.CompleteAdding();
        if (!_owner.Join(CloseWait))
        {
            return;
        }

        try
        {
            _connection?.Dispose();
        }
        catch
        {
            // Best effort on the way out.
        }
    }
}
