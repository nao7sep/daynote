using DayNote.Core.Identity;
using DayNote.Core.Time;
using Microsoft.Data.Sqlite;

namespace DayNote.Core.Storage;

/// <summary>
/// The backup store: a single SQLite database in write-ahead-logging mode holding every saved
/// notebook version as one row keyed by the notebook's case-insensitive path. Identical content is
/// deduplicated by hash, and recording is throttled to at most one version per notebook per
/// configured interval. It is a recovery net only — not a browsable history and not searchable.
/// </summary>
public sealed class BackupStore
{
    private readonly string _connectionString;

    public BackupStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
        }.ToString();

        EnsureSchema();
    }

    /// <summary>
    /// Records a backup version. Identical content is skipped (<see cref="BackupOutcome.Duplicate"/>).
    /// When <paramref name="force"/> is false, recording is also skipped if a version for this
    /// notebook was written within <paramref name="throttle"/> (<see cref="BackupOutcome.Throttled"/>).
    /// Closing a notebook passes <paramref name="force"/> = true so the final state is captured, but
    /// deduplication still applies.
    /// </summary>
    public BackupOutcome Record(string notebookKey, string content, DateTimeOffset nowUtc, TimeSpan throttle, bool force)
    {
        var hash = ContentHash.Sha256Hex(content);

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        if (HasContent(connection, transaction, notebookKey, hash))
        {
            transaction.Commit();
            return BackupOutcome.Duplicate;
        }

        if (!force)
        {
            var last = LastCreatedUtc(connection, transaction, notebookKey);
            if (last is { } lastTime && nowUtc - lastTime < throttle)
            {
                transaction.Commit();
                return BackupOutcome.Throttled;
            }
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                "insert into backup (id, notebook_key, created_utc, content_hash, content) " +
                "values ($id, $key, $created, $hash, $content)";
            insert.Parameters.AddWithValue("$id", IdGenerator.New());
            insert.Parameters.AddWithValue("$key", notebookKey);
            insert.Parameters.AddWithValue("$created", DayNoteTime.ToIso(nowUtc));
            insert.Parameters.AddWithValue("$hash", hash);
            insert.Parameters.AddWithValue("$content", content);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return BackupOutcome.Inserted;
    }

    /// <summary>Lists every stored version for a notebook, newest first.</summary>
    public IReadOnlyList<BackupVersion> List(string notebookKey)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "select id, created_utc, content_hash from backup where notebook_key = $key order by created_utc desc";
        command.Parameters.AddWithValue("$key", notebookKey);

        var versions = new List<BackupVersion>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            versions.Add(new BackupVersion(
                reader.GetString(0),
                DayNoteTime.ParseIso(reader.GetString(1)),
                reader.GetString(2)));
        }

        return versions;
    }

    /// <summary>Returns the exact stored content for a version, or null if it no longer exists.</summary>
    public string? GetContent(string id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "select content from backup where id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() as string;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Multiple application instances share this single database. A busy timeout makes a
        // concurrent writer wait briefly for the lock rather than failing immediately with
        // "database is locked".
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "pragma busy_timeout = 5000;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            pragma journal_mode = wal;
            create table if not exists backup (
              id           text primary key,
              notebook_key text not null,
              created_utc  text not null,
              content_hash text not null,
              content      text not null
            );
            create unique index if not exists backup_unique_key_hash on backup(notebook_key, content_hash);
            create index if not exists backup_key_time on backup(notebook_key, created_utc desc);
            """;
        command.ExecuteNonQuery();
    }

    private static bool HasContent(SqliteConnection connection, SqliteTransaction transaction, string notebookKey, string hash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select 1 from backup where notebook_key = $key and content_hash = $hash limit 1";
        command.Parameters.AddWithValue("$key", notebookKey);
        command.Parameters.AddWithValue("$hash", hash);
        return command.ExecuteScalar() is not null;
    }

    private static DateTimeOffset? LastCreatedUtc(SqliteConnection connection, SqliteTransaction transaction, string notebookKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select max(created_utc) from backup where notebook_key = $key";
        command.Parameters.AddWithValue("$key", notebookKey);
        return command.ExecuteScalar() is string text && !string.IsNullOrEmpty(text)
            ? DayNoteTime.ParseIso(text)
            : null;
    }
}
