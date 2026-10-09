using System.Security.Cryptography;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using DayNote.Core.Time;
using Microsoft.Data.Sqlite;

namespace DayNote.Core.Backup;

/// <summary>
/// The backup history (data-backup-conventions): one SQLite file, <c>backups.sqlite3</c>, directly
/// under DayNote's storage root (<c>DAYNOTE_DATA_DIR</c> or <c>~/.daynote</c>, resolved by
/// <see cref="AppPaths"/>). It keeps the last version of each protected file saved in each session, so a bug
/// that damages or deletes a binder, the settings or an attachment leaves an earlier version to restore by
/// hand. There is no startup scan, no timer, no exit capture and no restore path.
/// </summary>
/// <remarks>
/// <para>
/// Callers hand over the exact bytes they just published and return at once: one owner thread applies the
/// writes in the order they were recorded, so a save never waits on SQLite, and neither does the UI thread
/// a settings save runs on. A path recorded again before its earlier write is applied keeps only the newest
/// bytes. Ordinary quit drains the pending writes within a short bound (<see cref="Drain"/>); when the
/// operating system ends the session they are skipped, and the background thread ends with the process.
/// </para>
/// <para>
/// A session is one launch, identified by an id generated when the process starts. Each path keeps one row
/// per session: the session's first save of a path inserts it, later saves replace its content, and a first
/// save equal to the latest row from an earlier session writes nothing. Rows of earlier sessions are never
/// changed. The single-instance lease means one session writes at a time, and SQLite's locking covers the
/// rest.
/// </para>
/// <para>
/// It never breaks a save and never crashes the app. The save has already succeeded before
/// <see cref="Record"/> is called, so any failure here is logged once at <c>warn</c> and swallowed; a store
/// that cannot be opened disables recording for the session. Success logs nothing.
/// </para>
/// </remarks>
public static class BackupStore
{
    /// <summary>The one table. <c>content</c> is a BLOB of the exact bytes written, never decoded text.
    /// <c>written_at_utc</c> is the time of the latest save in the row, in the serialized ISO-8601-ms form
    /// (<c>2026-07-06T04:05:12.345Z</c>), a data value, never a filename stamp. <c>session_id</c> is null on
    /// rows recorded before sessions, which stay as earlier history.</summary>
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS backups (
          id             INTEGER PRIMARY KEY,
          session_id     TEXT,
          path           TEXT NOT NULL,
          content        BLOB NOT NULL,
          content_sha256 TEXT NOT NULL,
          byte_size      INTEGER NOT NULL,
          written_at_utc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_backups_path_id ON backups (path, id);
        CREATE UNIQUE INDEX IF NOT EXISTS idx_backups_path_session ON backups (path, session_id);
        """;

    // Format 1 kept a row per changed save; format 2 adds the session, keeping every earlier row.
    private const string AddSessions = """
        ALTER TABLE backups ADD COLUMN session_id TEXT;
        """;

    // Guards the pending writes, the owner's state and the session.
    private static readonly object Gate = new();
    private static readonly Dictionary<string, PendingWrite> Pending = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();
    private static Thread? _owner;
    private static bool _writing;
    private static string _session = IdGenerator.New();

    // Best-effort warn sink: (message, absolutePath, error). Null until the app installs one; a failure
    // before that is silently swallowed (the store must never depend on a logger being wired to be safe).
    private static Action<string, string, Exception>? _warn;

    // Touched only by the owner thread, and by Close while the owner is idle. Null means recording is
    // disabled for this session: not yet opened, or the open failed and its one warn was logged.
    private static SqliteConnection? _connection;
    private static bool _initialized;

    private sealed record PendingWrite(byte[] Bytes, DateTimeOffset WrittenAt, string Session);

    /// <summary>Installs the warn sink the store uses to log a record or open failure once. Called once at
    /// app startup, before any managed save. Optional: with no sink installed, a failure is swallowed
    /// silently rather than logged, but recording is never affected.</summary>
    public static void ConfigureWarn(Action<string, string, Exception> warn)
    {
        lock (Gate)
        {
            _warn = warn;
        }
    }

    /// <summary>
    /// Records one protected file the caller just published: <paramref name="absolutePath"/> is the full
    /// absolute path of the file as written, and <paramref name="bytes"/> the exact bytes written (the caller
    /// already holds them; the file is never reread). Returns at once; the owner thread applies the write.
    /// Never throws.
    /// </summary>
    public static void Record(string absolutePath, byte[] bytes)
    {
        var writtenAt = DateTimeOffset.UtcNow;
        lock (Gate)
        {
            if (!Pending.ContainsKey(absolutePath))
            {
                Order.Enqueue(absolutePath);
            }

            Pending[absolutePath] = new PendingWrite(bytes, writtenAt, _session);
            if (_owner is null)
            {
                _owner = new Thread(Own) { IsBackground = true, Name = "backups" };
                _owner.Start();
            }

            Monitor.PulseAll(Gate);
        }
    }

    /// <summary>
    /// Waits up to <paramref name="bound"/> for every write recorded so far to be applied. Returns false when
    /// the bound passed first; the writes then carry on until the process ends. Ordinary quit calls this; an
    /// ending OS session does not.
    /// </summary>
    public static bool Drain(TimeSpan bound)
    {
        var deadline = DateTime.UtcNow + bound;
        lock (Gate)
        {
            while (Order.Count > 0 || _writing)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                Monitor.Wait(Gate, remaining);
            }

            return true;
        }
    }

    // The owner thread: applies pending writes in the order they were first recorded.
    private static void Own()
    {
        while (true)
        {
            string path;
            PendingWrite write;
            lock (Gate)
            {
                while (Order.Count == 0)
                {
                    _writing = false;
                    Monitor.PulseAll(Gate);
                    Monitor.Wait(Gate);
                }

                _writing = true;
                path = Order.Dequeue();
                write = Pending[path];
                Pending.Remove(path);
            }

            Write(path, write);
        }
    }

    private static void Write(string path, PendingWrite write)
    {
        try
        {
            // The save has already landed, so the never-breaks-a-save boundary covers opening and path
            // resolution too, not just the SQL statements.
            var connection = EnsureOpen();
            if (connection is null)
            {
                return;
            }

            var hash = Sha256Hex(write.Bytes);
            using var transaction = connection.BeginTransaction(deferred: false);
            using (var latest = connection.CreateCommand())
            {
                latest.Transaction = transaction;
                latest.CommandText =
                    "SELECT content_sha256 FROM backups WHERE path = $path ORDER BY id DESC LIMIT 1";
                latest.Parameters.AddWithValue("$path", path);
                if (latest.ExecuteScalar() is string previousHash && previousHash == hash)
                {
                    // Unchanged since the latest recorded version, this session's or an earlier one's.
                    transaction.Commit();
                    return;
                }
            }

            using var upsert = connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText =
                "INSERT INTO backups (session_id, path, content, content_sha256, byte_size, written_at_utc) " +
                "VALUES ($session, $path, $content, $hash, $size, $writtenAt) " +
                "ON CONFLICT (path, session_id) DO UPDATE SET content = excluded.content, " +
                "content_sha256 = excluded.content_sha256, byte_size = excluded.byte_size, " +
                "written_at_utc = excluded.written_at_utc";
            upsert.Parameters.AddWithValue("$session", write.Session);
            upsert.Parameters.AddWithValue("$path", path);
            upsert.Parameters.AddWithValue("$content", write.Bytes);
            upsert.Parameters.AddWithValue("$hash", hash);
            upsert.Parameters.AddWithValue("$size", write.Bytes.LongLength);
            upsert.Parameters.AddWithValue("$writtenAt", DayNoteTime.ToIso(write.WrittenAt));
            upsert.ExecuteNonQuery();
            transaction.Commit();
        }
        catch (Exception ex)
        {
            WarnSafely("backup store: failed to record a managed write", path, ex);
        }
    }

    /// <summary>
    /// Opens and initializes the store once: checks its format version, converts a format-1 store, creates
    /// the table if absent, and switches on WAL and a short busy timeout. On any failure it logs one warn and
    /// leaves recording disabled for the session; it never throws.
    /// </summary>
    private static SqliteConnection? EnsureOpen()
    {
        if (_initialized)
        {
            return _connection;
        }

        _initialized = true;
        var file = "(unresolved)";
        SqliteConnection? connection = null;
        try
        {
            var paths = new AppPaths();
            file = paths.BackupStoreFile;
            // The store may be the first thing written under a fresh root; the root resolver creates it
            // (owner-only on POSIX) the same way on every path. The store is binary and written here, not
            // through the managed-text atomic write, so it never records itself.
            paths.EnsureCreated();

            connection = new SqliteConnection($"Data Source={file}");
            connection.Open();

            // First, before WAL or the schema can change the file: a store written by a newer DayNote, or one
            // DayNote did not write, is left exactly as it is, and recording stays disabled for the session.
            var found = SqliteFormatVersion.Claim(connection, FormatVersions.Backups, "backups.sqlite3");

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode = WAL;";
                pragma.ExecuteNonQuery();
                // Off the save path, a brief wait for the write lock costs nothing; longer contention skips
                // the write with one warn rather than holding the owner.
                pragma.CommandText = "PRAGMA busy_timeout = 1000;";
                pragma.ExecuteNonQuery();
            }

            if (found == 1)
            {
                using var transaction = connection.BeginTransaction(deferred: false);
                using var convert = connection.CreateCommand();
                convert.Transaction = transaction;
                convert.CommandText = AddSessions + $"PRAGMA user_version = {FormatVersions.Backups};";
                convert.ExecuteNonQuery();
                transaction.Commit();
            }

            using (var schema = connection.CreateCommand())
            {
                schema.CommandText = Schema;
                schema.ExecuteNonQuery();
            }

            _connection = connection;
        }
        catch (Exception ex)
        {
            WarnSafely("backup store: could not open; recording disabled for this session", file, ex);
            connection?.Dispose();
            _connection = null;
        }

        return _connection;
    }

    /// <summary>Reports a write the history could not take, such as an added file whose bytes could not be
    /// read for it. Never throws.</summary>
    internal static void Warn(string message, string path, Exception error) => WarnSafely(message, path, error);

    // The warn sink is itself an edge dependency. A broken sink cannot be allowed to undo the
    // backup layer's absolute never-breaks-a-save guarantee.
    private static void WarnSafely(string message, string path, Exception error)
    {
        Action<string, string, Exception>? warn;
        lock (Gate)
        {
            warn = _warn;
        }

        try
        {
            warn?.Invoke(message, path, error);
        }
        catch
        {
            // Nothing else is available at this logger-independent layer; the save still succeeds.
        }
    }

    /// <summary>
    /// Applies every pending write, closes the store and starts a new session. For tests, which need the
    /// rows written, the file handle released between throwaway roots, and a later launch modeled; the app
    /// itself lets the process exit close it. The next <see cref="Record"/> reopens against the current
    /// <c>DAYNOTE_DATA_DIR</c>.
    /// </summary>
    public static void Close()
    {
        lock (Gate)
        {
            while (Order.Count > 0 || _writing)
            {
                Monitor.Wait(Gate);
            }

            try
            {
                _connection?.Close();
                _connection?.Dispose();
            }
            catch
            {
                // Best effort: a close failure on teardown is harmless.
            }

            _connection = null;
            _initialized = false;
            _session = IdGenerator.New();
            // Microsoft.Data.Sqlite pools connections by connection string; clear the pool so the file
            // handle is actually released before a test deletes its throwaway root.
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>SHA-256 of the exact bytes, lowercase hex.</summary>
    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
