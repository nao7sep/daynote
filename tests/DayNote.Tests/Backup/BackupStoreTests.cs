using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DayNote.Core.Backup;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using DayNote.Tests.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DayNote.Tests.Backup;

/// <summary>
/// The write-through data-backup store (data-backup conventions), pinned to the guarantees that make it a
/// trustworthy safety net: <c>content</c> is a byte-identical BLOB (CR/LF and a non-UTF-8 byte survive),
/// <c>written_at_utc</c> is the serialized ISO-8601-ms form (NOT the filename stamp), dedup skips an
/// unchanged re-save while a changed save and a revert each insert a row, and the whole thing is
/// best-effort — an injected store failure never throws, logs one warn, and never breaks the save.
/// </summary>
/// <remarks>
/// Records reach the store only through the atomic writer, so each test drives a real
/// <see cref="AtomicFile.WriteAllText"/> under a throwaway <c>DAYNOTE_HOME</c> and reads the resulting
/// <c>backups.sqlite3</c> back with a direct read-only connection. Joined to the AppPaths collection so the
/// process-wide env var never races; the store singleton is closed in teardown so it re-opens per root.
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

    // ----- Dedup ---------------------------------------------------------------------------------

    [Fact]
    public void An_unchanged_re_save_is_deduped_and_writes_no_new_row()
    {
        AtomicFile.WriteAllText(TargetPath, "same content");
        AtomicFile.WriteAllText(TargetPath, "same content");
        AtomicFile.WriteAllText(TargetPath, "same content");

        Assert.Equal(1, RowCount(TargetPath));
    }

    [Fact]
    public void A_changed_save_inserts_a_new_row()
    {
        AtomicFile.WriteAllText(TargetPath, "version one");
        AtomicFile.WriteAllText(TargetPath, "version two");

        Assert.Equal(2, RowCount(TargetPath));
    }

    [Fact]
    public void A_revert_to_an_earlier_value_inserts_a_row_because_it_differs_from_the_preceding()
    {
        AtomicFile.WriteAllText(TargetPath, "A");
        AtomicFile.WriteAllText(TargetPath, "B");
        AtomicFile.WriteAllText(TargetPath, "A"); // reverts to the first value

        // Dedup compares only against the immediately preceding row (B), so the revert to A is recorded
        // as the distinct version it is — three rows, not two.
        Assert.Equal(3, RowCount(TargetPath));
        Assert.Equal("A", Encoding.UTF8.GetString(LatestRow(TargetPath)!.Content));
    }

    [Fact]
    public void Different_paths_dedup_independently()
    {
        var other = Path.Combine(_home, "state.json");
        AtomicFile.WriteAllText(TargetPath, "shared");
        AtomicFile.WriteAllText(other, "shared"); // same content, different path — its own first row

        Assert.Equal(1, RowCount(TargetPath));
        Assert.Equal(1, RowCount(other));
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

        Assert.Null(exception); // never throws
        Assert.Equal("the save must survive", File.ReadAllText(TargetPath)); // the save landed
        Assert.Single(warnings); // exactly one warn, once for the session
        Assert.Contains("recording disabled", warnings[0]);

        // A second save while the store is still broken must NOT log again (disabled for the session).
        AtomicFile.WriteAllText(TargetPath, "and a second save too");
        Assert.Equal("and a second save too", File.ReadAllText(TargetPath));
        Assert.Single(warnings); // still one — no re-log of the broken open
    }

    // ----- Reading the store ---------------------------------------------------------------------

    private sealed record BackupRow(string Path, byte[] Content, string ContentSha256, long ByteSize, string WrittenAtUtc);

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
