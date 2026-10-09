using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using DayNote.Core.Backup;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// Every binder, config, and state write goes through the atomic writer, so its guarantees matter:
/// the target ends up with exactly the supplied content (UTF-8, no BOM), an existing file is replaced
/// in full, and the temp file used for the write-then-rename is never left behind.
/// </summary>
/// <remarks>
/// The atomic writer is where saves reach the backup history (<see cref="BackupStore.Record"/> fires after
/// each rename lands), so <c>DAYNOTE_DATA_DIR</c> is relocated to this test's throwaway directory — otherwise
/// the store would open under the developer's real <c>~/.daynote/</c>. Joined to the AppPaths collection
/// so that process-wide env var never races another test; the store singleton is closed in teardown so it
/// re-opens per throwaway root. Content assertions filter the store's own <c>backups.sqlite3</c>(+wal/shm).
/// </remarks>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;
    private readonly string? _previousHome;

    public AtomicFileTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daynote-atomic-tests-" + IdGenerator.New());
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "data.txt");

        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _directory);
    }

    /// <summary>The files under <see cref="_directory"/>, excluding the backup history and its
    /// <c>-wal</c>/<c>-shm</c> sidecars — normal SQLite artifacts that sit beside a relocated root, not
    /// output of the code under test.</summary>
    private string[] NonStoreFiles() =>
        Directory.GetFiles(_directory)
            .Where(f => !Path.GetFileName(f).StartsWith("backups.sqlite3", StringComparison.Ordinal))
            .ToArray();

    [Fact]
    public void Writes_the_exact_content()
    {
        AtomicFile.WriteAllText(_path, "line one\nline two\n");

        Assert.Equal("line one\nline two\n", File.ReadAllText(_path, Encoding.UTF8));
    }

    [Fact]
    public void Overwrites_an_existing_file_in_full()
    {
        AtomicFile.WriteAllText(_path, "a much longer original content");
        AtomicFile.WriteAllText(_path, "short");

        Assert.Equal("short", File.ReadAllText(_path, Encoding.UTF8));
    }

    [Fact]
    public void Writes_utf8_without_a_byte_order_mark()
    {
        AtomicFile.WriteAllText(_path, "日本語");

        var bytes = File.ReadAllBytes(_path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("日本語", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Leaves_no_temp_file_behind()
    {
        AtomicFile.WriteAllText(_path, "content");

        Assert.Equal(new[] { _path }, NonStoreFiles());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Writing_what_the_file_already_holds_leaves_it_and_its_history_alone()
    {
        AtomicFile.WriteAllText(_path, "same");
        var earlier = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_path, earlier);

        AtomicFile.WriteAllText(_path, "same");

        Assert.Equal(earlier, File.GetLastWriteTimeUtc(_path));
        Assert.Equal(1, BackupRows());

        AtomicFile.WriteAllText(_path, "changed");

        Assert.Equal("changed", File.ReadAllText(_path, Encoding.UTF8));
        Assert.NotEqual(earlier, File.GetLastWriteTimeUtc(_path));
        Assert.Equal(2, BackupRows());
    }

    [MacOnlyFact]
    [SupportedOSPlatform("macos")]
    public void A_changed_write_keeps_the_files_mode_and_gets_a_fresh_modified_time()
    {
        AtomicFile.WriteAllText(_path, "one");
        const UnixFileMode restricted = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(_path, restricted);
        var earlier = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_path, earlier);

        AtomicFile.WriteAllText(_path, "two");

        Assert.Equal("two", File.ReadAllText(_path, Encoding.UTF8));
        Assert.Equal(restricted, File.GetUnixFileMode(_path));
        Assert.NotEqual(earlier, File.GetLastWriteTimeUtc(_path));
    }

    [Fact]
    public void A_copy_keeps_the_sources_modified_time_and_leaves_no_temp_file()
    {
        var source = Path.Combine(_directory, "source.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        var modified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, modified);
        var copy = Path.Combine(_directory, "copy.bin");

        AtomicFile.CopyNew(source, copy);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(copy));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(copy));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void A_copy_records_nothing_itself()
    {
        // The caller records an added file with the hash of what it copied (BackupStore.RecordAdded); the
        // copy alone never reaches the history.
        var source = Path.Combine(_directory, "photo.bin");
        File.WriteAllBytes(source, [0, 0xFF, 0x0D, 0x0A, 7]);
        var copy = Path.Combine(_directory, "assets", "photo.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);

        AtomicFile.CopyNew(source, copy);
        AtomicFile.WriteAllText(_path, "opens the history");
        BackupStore.Close();

        Assert.Empty(BackupContents(copy));
    }

    [Fact]
    public void A_failed_copy_records_nothing()
    {
        var source = Path.Combine(_directory, "source.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        var taken = Path.Combine(_directory, "taken.bin");
        File.WriteAllBytes(taken, [9]);

        Assert.ThrowsAny<IOException>(() => AtomicFile.CopyNew(source, taken));
        AtomicFile.WriteAllText(_path, "opens the history");
        BackupStore.Close();

        Assert.Empty(BackupContents(taken));
    }

    private List<byte[]> BackupContents(string path)
    {
        var contents = new List<byte[]>();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={new AppPaths().BackupStoreFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content FROM backups WHERE path = $path ORDER BY id";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            contents.Add((byte[])reader["content"]);
        }

        return contents;
    }

    [Fact]
    public void A_copy_never_replaces_an_existing_file_and_removes_its_temp_file()
    {
        var source = Path.Combine(_directory, "source.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        var taken = Path.Combine(_directory, "taken.bin");
        File.WriteAllBytes(taken, [9]);

        Assert.ThrowsAny<IOException>(() => AtomicFile.CopyNew(source, taken));

        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(taken));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Creates_missing_parent_directories()
    {
        var nested = Path.Combine(_directory, "a", "b", "data.txt");

        AtomicFile.WriteAllText(nested, "deep");

        Assert.Equal("deep", File.ReadAllText(nested, Encoding.UTF8));
    }

    private int BackupRows()
    {
        BackupStore.Close();
        using var connection = new SqliteConnection($"Data Source={new AppPaths().BackupStoreFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM backups WHERE path = $path";
        command.Parameters.AddWithValue("$path", Path.GetFullPath(_path));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        // Close the store so its singleton releases the file handle and re-opens per throwaway root.
        BackupStore.Close();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp directory is harmless.
        }
    }
}
