using System;
using System.IO;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>The read-only header check a store that opens later is held to at launch.</summary>
public sealed class FileHeaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daynote-header-" + IdGenerator.New());
    private string DatabasePath => Path.Combine(_directory, "backups.sqlite3");

    public FileHeaderTests() => Directory.CreateDirectory(_directory);

    private void Run(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private Exception? Check() => FileHeader.CheckSqliteFile(DatabasePath, FileFormats.Backups, "backups.sqlite3");

    [Fact]
    public void A_missing_or_empty_database_passes()
    {
        Assert.Null(Check());
        Run("SELECT 1;");
        Assert.Null(Check());
    }

    [Fact]
    public void A_database_with_its_header_passes()
    {
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            FileHeader.ClaimSqlite(connection, FileFormats.Backups, "backups.sqlite3");
        }

        Assert.Null(Check());
    }

    [Theory]
    [InlineData("CREATE TABLE t (x); PRAGMA user_version = 2;", typeof(NewerFormatException))]
    [InlineData("CREATE TABLE t (x); PRAGMA user_version = 1;", typeof(InvalidDataException))] // no file_header
    [InlineData("CREATE TABLE t (x);", typeof(InvalidDataException))] // no version
    [InlineData("""
        CREATE TABLE file_header (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        INSERT INTO file_header VALUES ('format.owner', 'daynote'), ('format.kind', 'records');
        PRAGMA user_version = 1;
        """, typeof(InvalidDataException))] // another kind
    public void A_refused_database_is_reported_and_left_byte_identical(string sql, Type refusal)
    {
        Run(sql);
        var before = File.ReadAllBytes(DatabasePath);

        Assert.IsType(refusal, Check());

        Assert.Equal(before, File.ReadAllBytes(DatabasePath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void A_file_that_is_not_a_database_is_unreadable_and_left_byte_identical()
    {
        File.WriteAllText(DatabasePath, "not a database");

        Assert.NotNull(Check());

        Assert.Equal("not a database", File.ReadAllText(DatabasePath));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
