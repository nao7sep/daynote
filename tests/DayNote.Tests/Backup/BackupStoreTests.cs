using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DayNote.Core.Backup;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using DayNote.Core.Time;
using DayNote.Tests.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DayNote.Tests.Backup;

/// <summary>
/// The backup history (data-backup-conventions), pinned to the guarantees that make it a trustworthy
/// safety net: <c>content</c> is a byte-identical BLOB (CR/LF and a non-UTF-8 byte survive),
/// <c>written_at_utc</c> is the serialized ISO-8601-ms form (not the filename stamp), each path keeps one
/// row per session holding its last save, a session adds a row only when the content changed, earlier rows
/// are never changed, and the whole thing is best effort: a store failure never throws, logs one warn, and
/// never breaks or delays the save.
/// </summary>
/// <remarks>
/// Each test drives a real <see cref="AtomicFile.WriteAllText"/> under a throwaway <c>DAYNOTE_DATA_DIR</c>
/// and reads <c>backups.sqlite3</c> back with a direct read-only connection after <see cref="BackupStore.Close"/>,
/// which applies the pending writes and starts a new session, as a later launch would. Joined to the AppPaths
/// collection so the process-wide env var never races.
/// </remarks>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class BackupStoreTests : IDisposable
{
    private readonly string _home;
    private readonly string? _previousHome;
    private readonly AppPaths _paths;

    public BackupStoreTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "daynote-backupstore-tests-" + IdGenerator.New());
        Directory.CreateDirectory(_home);
        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _home);
        _paths = new AppPaths();
    }

    private string TargetPath => Path.Combine(_home, "config.json");

    // ----- Byte fidelity ------------------------------------------------------------------------

    [Fact]
    public void Content_is_stored_byte_identical_including_crlf_and_a_high_byte_multibyte_sequence()
    {
        // The app's managed-text writes are strings encoded as UTF-8-no-BOM, so byte fidelity here means:
        // the BLOB is those exact bytes, never text that was decoded and re-encoded (which would normalize
        // CR/LF or a BOM). Two proofs: a CR/LF pair whose CR byte (0x0D) must survive, and 'ÿ' (U+00FF),
        // which UTF-8-encodes to the high-byte sequence 0xC3 0xBF — bytes a naive Latin-1/ASCII re-decode
        // would corrupt. The BLOB must equal the exact bytes on disk, byte-for-byte.
        var content = "first line\r\nsecond line\r\nÿ end";
        var expected = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);

        AtomicFile.WriteAllText(TargetPath, content);
        var row = LatestRow(TargetPath);

        Assert.NotNull(row);
        Assert.Equal(expected, row!.Content);
        // The bytes on disk are what was recorded — byte-for-byte, no re-read.
        Assert.Equal(File.ReadAllBytes(TargetPath), row.Content);
        Assert.Equal(expected.Length, row.ByteSize);
        // The CR byte really is present (a CR/LF-normalizing path would have dropped it).
        Assert.Contains((byte)0x0D, row.Content);
        // The high-byte UTF-8 sequence for 'ÿ' really is present, intact (0xC3 0xBF).
        var index = IndexOf(row.Content, new byte[] { 0xC3, 0xBF });
        Assert.True(index >= 0, "the 0xC3 0xBF UTF-8 bytes for 'ÿ' must be stored verbatim");
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public void Content_sha256_is_over_the_raw_bytes()
    {
        AtomicFile.WriteAllText(TargetPath, "hash me\r\n");
        var row = LatestRow(TargetPath)!;

        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(row.Content));
        Assert.Equal(expected, row.ContentSha256);
    }

    [Fact]
    public void Path_is_the_full_absolute_path()
    {
        AtomicFile.WriteAllText(TargetPath, "x");
        var row = LatestRow(TargetPath)!;

        Assert.Equal(Path.GetFullPath(TargetPath), row.Path);
        Assert.True(Path.IsPathRooted(row.Path));
    }

    // ----- written_at_utc shape ------------------------------------------------------------------

    private static readonly Regex IsoMs = new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", RegexOptions.Compiled);
    private static readonly Regex FilenameStamp = new(@"^\d{8}-\d{6}-\d{3}-utc$", RegexOptions.Compiled);

    [Fact]
    public void Written_at_utc_is_the_serialized_iso_ms_form_not_the_filename_stamp()
    {
        AtomicFile.WriteAllText(TargetPath, "y");
        var row = LatestRow(TargetPath)!;

        // Serialized ISO-8601 with milliseconds and a trailing Z (2026-07-06T04:05:12.345Z).
        Assert.Matches(IsoMs, row.WrittenAtUtc);
        // It is emphatically NOT the yyyymmdd-hhmmss-fff-utc filename stamp.
        Assert.DoesNotMatch(FilenameStamp, row.WrittenAtUtc);
        // And it round-trips as a real timestamp.
        Assert.True(DateTimeOffset.TryParse(row.WrittenAtUtc, out _));
    }

    // ----- One row per path per session ---------------------------------------------------------

    [Fact]
    public void Saves_in_one_session_leave_one_row_holding_the_last_content()
    {
        AtomicFile.WriteAllText(TargetPath, "version one");
        AtomicFile.WriteAllText(TargetPath, "version two");
        AtomicFile.WriteAllText(TargetPath, "version three");

        Assert.Equal(1, RowCount(TargetPath));
        Assert.Equal("version three", Encoding.UTF8.GetString(LatestRow(TargetPath)!.Content));
    }

    [Fact]
    public void A_later_session_adds_a_row_only_when_the_content_changed()
    {
        AtomicFile.WriteAllText(TargetPath, "A");
        BackupStore.Close(); // the next launch

        AtomicFile.WriteAllText(TargetPath, "A");
        BackupStore.Close();
        Assert.Equal(1, RowCount(TargetPath));

        AtomicFile.WriteAllText(TargetPath, "B");
        Assert.Equal(2, RowCount(TargetPath));
    }

    [Fact]
    public void A_later_session_never_changes_an_earlier_sessions_row()
    {
        AtomicFile.WriteAllText(TargetPath, "first launch");
        BackupStore.Close();

        AtomicFile.WriteAllText(TargetPath, "second launch, first save");
        AtomicFile.WriteAllText(TargetPath, "second launch, last save");

        Assert.Equal(new[] { "first launch", "second launch, last save" }, Contents(TargetPath));
    }

    [Fact]
    public void Writes_to_one_path_apply_in_save_order()
    {
        for (var i = 0; i < 50; i++)
        {
            BackupStore.Record(Path.GetFullPath(TargetPath), Encoding.UTF8.GetBytes($"save {i}"));
        }

        Assert.Equal("save 49", Encoding.UTF8.GetString(LatestRow(TargetPath)!.Content));
    }

    [Fact]
    public void A_save_does_not_wait_while_another_connection_holds_the_write_lock()
    {
        AtomicFile.WriteAllText(TargetPath, "opens the store");
        Assert.True(BackupStore.Drain(TimeSpan.FromSeconds(5)));

        using var other = new SqliteConnection($"Data Source={_paths.BackupStoreFile};Pooling=False");
        other.Open();
        using var held = other.BeginTransaction(deferred: false);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        AtomicFile.WriteAllText(TargetPath, "saved while the history is locked");
        stopwatch.Stop();

        Assert.Equal("saved while the history is locked", File.ReadAllText(TargetPath));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500), $"the save took {stopwatch.Elapsed}");
        // The drain is bounded: the history's own wait for the lock outlasts it.
        Assert.False(BackupStore.Drain(TimeSpan.FromMilliseconds(200)));
        held.Rollback();
    }

    [Fact]
    public void Different_paths_keep_their_own_rows()
    {
        var other = Path.Combine(_home, "state.json");
        AtomicFile.WriteAllText(TargetPath, "shared");
        AtomicFile.WriteAllText(other, "shared"); // same content, different path — its own first row

        Assert.Equal(1, RowCount(TargetPath));
        Assert.Equal(1, RowCount(other));
    }

    [Fact]
    public void Opting_out_keeps_the_live_write_and_records_no_backup_row()
    {
        AtomicFile.WriteAllText(TargetPath, "configuration");
        var statePath = Path.Combine(_home, "state.json");
        AtomicFile.WriteAllText(statePath, "state one", recordBackup: false);
        AtomicFile.WriteAllText(statePath, "state two", recordBackup: false);
        Assert.Equal("state two", File.ReadAllText(statePath));
        Assert.Equal(0, RowCount(statePath));
        Assert.Equal(1, RowCount(TargetPath));
        Assert.Empty(Directory.GetFiles(_home, "*.tmp"));
    }

    // ----- Best-effort: a store failure never breaks the save ------------------------------------

    [Fact]
    public void A_store_open_failure_does_not_throw_logs_one_warn_and_the_save_still_lands()
    {
        // Make the store impossible to open: occupy backups.sqlite3 with a *directory*, so opening it as a
        // database file fails. The failure must be swallowed with exactly one warn, and the atomic write
        // it hangs off must still succeed — the save already landed before the record is attempted.
        Directory.CreateDirectory(_paths.BackupStoreFile);
        var warnings = new List<string>();
        BackupStore.ConfigureWarn((message, _, _) => warnings.Add(message));

        var exception = Record.Exception(() => AtomicFile.WriteAllText(TargetPath, "the save must survive"));
        Assert.True(BackupStore.Drain(TimeSpan.FromSeconds(5)));

        Assert.Null(exception); // never throws
        Assert.Equal("the save must survive", File.ReadAllText(TargetPath)); // the save landed
        Assert.Single(warnings); // exactly one warn, once for the session
        Assert.Contains("recording disabled", warnings[0]);

        // A second save while the store is still broken must NOT log again (disabled for the session).
        AtomicFile.WriteAllText(TargetPath, "and a second save too");
        Assert.True(BackupStore.Drain(TimeSpan.FromSeconds(5)));
        Assert.Equal("and a second save too", File.ReadAllText(TargetPath));
        Assert.Single(warnings); // still one — no re-log of the broken open
    }

    [Fact]
    public void A_throwing_warn_sink_cannot_break_an_already_landed_save()
    {
        Directory.CreateDirectory(_paths.BackupStoreFile);
        BackupStore.ConfigureWarn((_, _, _) => throw new InvalidOperationException("broken warn sink"));

        var exception = Record.Exception(() => AtomicFile.WriteAllText(TargetPath, "the save must survive"));
        Assert.True(BackupStore.Drain(TimeSpan.FromSeconds(5)));

        Assert.Null(exception);
        Assert.Equal("the save must survive", File.ReadAllText(TargetPath));
    }

    [Fact]
    public void A_storage_root_resolution_failure_does_not_throw_and_the_save_still_lands()
    {
        const string missingVariable = "DAYNOTE_BACKUP_TEST_MISSING_ROOT";
        var previousMissingValue = Environment.GetEnvironmentVariable(missingVariable);
        Environment.SetEnvironmentVariable(missingVariable, null);
        BackupStore.Close();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, "$" + missingVariable);
        var warnings = new List<string>();
        BackupStore.ConfigureWarn((message, _, _) => warnings.Add(message));

        try
        {
            var exception = Record.Exception(() => AtomicFile.WriteAllText(TargetPath, "the save must survive"));
            Assert.True(BackupStore.Drain(TimeSpan.FromSeconds(5)));

            Assert.Null(exception);
            Assert.Equal("the save must survive", File.ReadAllText(TargetPath));
            Assert.Single(warnings);
            Assert.Contains("recording disabled", warnings[0]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(missingVariable, previousMissingValue);
            Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _home);
        }
    }

    // ----- Format version (PRAGMA user_version) --------------------------------------------------

    [Fact]
    public void A_new_store_records_the_current_format_version()
    {
        AtomicFile.WriteAllText(TargetPath, "first");

        Assert.Equal(FormatVersions.Backups, UserVersion());
        Assert.Equal(1, RowCount(TargetPath));
    }

    [Theory]
    [InlineData(0)] // tables but no version: unreadable
    [InlineData(-1)] // a version no DayNote writes
    [InlineData(FormatVersions.Backups + 1)]
    public void An_unversioned_or_newer_store_is_left_byte_identical_with_one_warn_and_the_save_still_lands(long userVersion)
    {
        using (var connection = new SqliteConnection($"Data Source={_paths.BackupStoreFile};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE backups (id INTEGER PRIMARY KEY); PRAGMA user_version = {userVersion};";
            command.ExecuteNonQuery();
        }

        var before = File.ReadAllBytes(_paths.BackupStoreFile);
        var warnings = new List<string>();
        BackupStore.ConfigureWarn((message, _, _) => warnings.Add(message));

        AtomicFile.WriteAllText(TargetPath, "the save must survive");
        AtomicFile.WriteAllText(TargetPath, "and a second save too");
        BackupStore.Close();

        Assert.Equal("and a second save too", File.ReadAllText(TargetPath));
        Assert.Contains("recording disabled", Assert.Single(warnings));
        Assert.Equal(before, File.ReadAllBytes(_paths.BackupStoreFile));
        Assert.False(File.Exists(_paths.BackupStoreFile + "-wal"));
    }

    [Fact]
    public void A_format_1_store_is_converted_keeping_its_rows()
    {
        using (var connection = new SqliteConnection($"Data Source={_paths.BackupStoreFile};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE backups (id INTEGER PRIMARY KEY, path TEXT NOT NULL, content BLOB NOT NULL,
                  content_sha256 TEXT NOT NULL, byte_size INTEGER NOT NULL, written_at_utc TEXT NOT NULL);
                CREATE INDEX idx_backups_path_id ON backups (path, id);
                INSERT INTO backups (path, content, content_sha256, byte_size, written_at_utc)
                  VALUES ($path, X'6F6C64', 'x', 3, '2026-07-06T04:05:12.345Z'),
                         ($path, X'6F6C646572', 'y', 5, '2026-07-07T04:05:12.345Z');
                PRAGMA user_version = 1;
                """;
            command.Parameters.AddWithValue("$path", Path.GetFullPath(TargetPath));
            command.ExecuteNonQuery();
        }

        AtomicFile.WriteAllText(TargetPath, "new");
        AtomicFile.WriteAllText(TargetPath, "newer");

        Assert.Equal(new[] { "old", "older", "newer" }, Contents(TargetPath));
        Assert.Equal(FormatVersions.Backups, UserVersion());
    }

    private long UserVersion()
    {
        BackupStore.Close();
        using var connection = new SqliteConnection($"Data Source={_paths.BackupStoreFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)command.ExecuteScalar()!;
    }

    // ----- Reading the store ---------------------------------------------------------------------

    private sealed record BackupRow(string Path, byte[] Content, string ContentSha256, long ByteSize, string WrittenAtUtc);

    private string[] Contents(string path)
    {
        BackupStore.Close();
        using var connection = new SqliteConnection($"Data Source={_paths.BackupStoreFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM backups WHERE path = $path ORDER BY id";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        using var reader = command.ExecuteReader();
        var contents = new List<string>();
        while (reader.Read())
        {
            contents.Add(Encoding.UTF8.GetString((byte[])reader["content"]));
        }

        return contents.ToArray();
    }

    private BackupRow? LatestRow(string path)
    {
        // Close the singleton so its handle is released before we open our own read-only connection.
        BackupStore.Close();
        using var connection = new SqliteConnection($"Data Source={_paths.BackupStoreFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT path, content, content_sha256, byte_size, written_at_utc " +
            "FROM backups WHERE path = $path ORDER BY id DESC LIMIT 1";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var content = (byte[])reader["content"];
        return new BackupRow(
            reader.GetString(0), content, reader.GetString(2), reader.GetInt64(3), reader.GetString(4));
    }

    private int RowCount(string path)
    {
        BackupStore.Close();
        using var connection = new SqliteConnection($"Data Source={_paths.BackupStoreFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM backups WHERE path = $path";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        // Restore the default (no-op) warn sink so an injected sink from one test never leaks into another.
        BackupStore.ConfigureWarn((_, _, _) => { });
        BackupStore.Close();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp directory is harmless.
        }
    }
}
