using System.Security.Cryptography;
using DayNote.Core.Storage;
using DayNote.Core.Time;
using Microsoft.Data.Sqlite;

namespace DayNote.Core.Backup;

/// <summary>
/// The write-through data-backup store (data-backup conventions). It owns one add-only SQLite file,
/// <c>backups.sqlite3</c>, directly under DayNote's storage root (<c>DAYNOTE_HOME</c> or <c>~/.daynote</c>,
/// resolved in one place by <see cref="AppPaths"/> — never a hardcoded path). Every managed <em>text</em>
/// save records the exact bytes it just wrote here, strictly AFTER its atomic rename lands, so the history
/// is always as current as the last save. There is no startup scan, no periodic pass, no restore path.
/// </summary>
/// <remarks>
/// <para>
/// SQLite binding: <c>Microsoft.Data.Sqlite</c> — the .NET-native managed provider with a bundled native
/// <c>e_sqlite3</c> (via SQLitePCLRaw), so it needs no native rebuild and adds no packaging churn. A BLOB
/// round-trips through <c>byte[]</c>, so CR/LF, a BOM, and non-UTF-8 bytes are stored byte-identically.
/// </para>
/// <para>Two absolute musts drive every line below (they are not best-effort aspirations):</para>
/// <list type="bullet">
///   <item>It never breaks a save and never crashes the app. The save has already succeeded — the file is
///   on disk before <see cref="Record"/> is called — so any failure here (the DB is locked, the disk is
///   full, an insert throws) is caught, logged once at <c>warn</c>, and swallowed. A lost record self-heals
///   on the next save of that file, whose content will differ from the last recorded row.</item>
///   <item>It logs only failures. A successful record logs NOTHING; a line per save would flood the log.</item>
/// </list>
/// <para>
/// The store is a process-wide singleton opened once, best-effort. The one edge concern it carries — the
/// warn log on failure — is injected as a delegate so DayNote.Core stays UI- and logger-framework-free;
/// the desktop app installs its logger once at startup via <see cref="ConfigureWarn"/>.
/// </para>
/// </remarks>
public static class BackupStore
{
    /// <summary>The one add-only table. <c>content</c> is a BLOB of the exact bytes written — never
    /// decoded text — so CR/LF, a BOM, and non-UTF-8 bytes are stored byte-identically. <c>written_at_utc</c>
    /// is the serialized ISO-8601-ms form (<c>2026-07-06T04:05:12.345Z</c>), a data value — NEVER the
    /// <c>yyyymmdd-hhmmss-fff-utc</c> filename stamp. The <c>(path, id)</c> index serves the latest-row-per-
    /// path dedup lookup.</summary>
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS backups (
          id             INTEGER PRIMARY KEY,
          path           TEXT NOT NULL,
          content        BLOB NOT NULL,
          content_sha256 TEXT NOT NULL,
          byte_size      INTEGER NOT NULL,
          written_at_utc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_backups_path_id ON backups (path, id);
        """;

    private static readonly object Gate = new();

    // Best-effort warn sink: (message, absolutePath, error). Null until the app installs one; a failure
    // before that is silently swallowed (the store must never depend on a logger being wired to be safe).
    private static Action<string, string, Exception>? _warn;

    // The single connection, opened once. Null means recording is disabled for this session — either not
    // yet opened, or the open failed (a single warn was already logged) — so every later Record becomes a
    // no-op rather than retrying (and re-logging) a broken open on every save.
    private static SqliteConnection? _connection;
    private static bool _initialized;

    /// <summary>Installs the warn sink the store uses to log a record/open failure once. Called once at
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
    /// Record one managed-text write: <paramref name="absolutePath"/> is the FULL absolute path of the
    /// file as written; <paramref name="bytes"/> is the exact raw bytes just written (the caller already
    /// holds them — never re-read the file).
    /// </summary>
    /// <remarks>
    /// Dedup by content hash per path: the new content's SHA-256 is compared against the latest row for
    /// the same <c>path</c>, and the insert is SKIPPED when they are equal. This collapses consecutive
    /// identical saves (an autosave with no real change writes no row) while still recording every
    /// genuinely distinct version — including a revert, whose content differs from the immediately
    /// preceding row. Best-effort and silent on success; any failure is caught, logged once at
    /// <c>warn</c> (path + reason), and swallowed. It never throws, never crashes the app, never breaks
    /// the save.
    /// </remarks>
    public static void Record(string absolutePath, byte[] bytes)
    {
        lock (Gate)
        {
            var connection = EnsureOpen();
            if (connection is null)
            {
                return; // open failed earlier; disabled for the session (already warned once)
            }

            try
            {
                var hash = Sha256Hex(bytes);

                using (var latest = connection.CreateCommand())
                {
                    latest.CommandText =
                        "SELECT content_sha256 FROM backups WHERE path = $path ORDER BY id DESC LIMIT 1";
                    latest.Parameters.AddWithValue("$path", absolutePath);
                    if (latest.ExecuteScalar() is string previousHash && previousHash == hash)
                    {
                        return; // unchanged since the last recorded version — dedup skip
                    }
                }

                using var insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO backups (path, content, content_sha256, byte_size, written_at_utc) " +
                    "VALUES ($path, $content, $hash, $size, $writtenAt)";
                insert.Parameters.AddWithValue("$path", absolutePath);
                insert.Parameters.AddWithValue("$content", bytes);
                insert.Parameters.AddWithValue("$hash", hash);
                insert.Parameters.AddWithValue("$size", bytes.LongLength);
                insert.Parameters.AddWithValue("$writtenAt", DayNoteTime.ToIso(DateTimeOffset.UtcNow));
                insert.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _warn?.Invoke("backup store: failed to record a managed write", absolutePath, ex);
            }
        }
    }

    /// <summary>
    /// Open and initialize the store once (create the table if absent, switch on WAL and a busy timeout).
    /// Best-effort: on any failure it logs ONE warn, leaves recording disabled for the session, and never
    /// throws. WAL is what lets the tolerated two-instance case (two DayNote windows writing at once)
    /// serialize safely without a cross-process lock; the busy timeout makes a contended writer wait for
    /// the write lock rather than immediately failing and dropping that record.
    /// </summary>
    private static SqliteConnection? EnsureOpen()
    {
        if (_initialized)
        {
            return _connection;
        }

        _initialized = true;
        var file = new AppPaths().BackupStoreFile;
        try
        {
            // The first writer under the root does the mkdir -p (storage-path convention); the store may
            // be the first thing written on a fresh root.
            // not recorded: backups.sqlite3 is the store itself — binary, and written by this backup layer,
            // not through the managed-text atomic-write path — so it never records itself. No recursion,
            // no special case (data-backup conventions: "A binary store, excluded from itself").
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            var connection = new SqliteConnection($"Data Source={file}");
            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode = WAL;";
                pragma.ExecuteNonQuery();
                // busy_timeout: under the tolerated two-instance case, a contended write waits up to this
                // long (~5s) for SQLite's write lock instead of immediately failing with SQLITE_BUSY and
                // dropping that record.
                pragma.CommandText = "PRAGMA busy_timeout = 5000;";
                pragma.ExecuteNonQuery();
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
            _warn?.Invoke("backup store: could not open; recording disabled for this session", file, ex);
            _connection = null;
        }

        return _connection;
    }

    /// <summary>
    /// Close the store (best-effort). For tests that need to release the file handle between throwaway
    /// roots; the app itself lets the process exit close it. Resets the singleton so the next
    /// <see cref="Record"/> re-opens against the current <c>DAYNOTE_HOME</c>.
    /// </summary>
    public static void Close()
    {
        lock (Gate)
        {
            try
            {
                _connection?.Close();
                _connection?.Dispose();
            }
            catch
            {
                // best-effort: a close failure on shutdown/teardown is harmless
            }

            _connection = null;
            _initialized = false;
            // Microsoft.Data.Sqlite pools connections by connection string; clear the pool so the file
            // handle is actually released before a test deletes its throwaway root.
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>SHA-256 of the exact bytes, lowercase hex.</summary>
    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
