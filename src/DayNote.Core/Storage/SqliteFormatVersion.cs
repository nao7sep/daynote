using Microsoft.Data.Sqlite;

namespace DayNote.Core.Storage;

/// <summary>
/// A SQLite store's format version, kept in <c>PRAGMA user_version</c> (store-recovery-conventions).
/// </summary>
public static class SqliteFormatVersion
{
    /// <summary>
    /// Checks the open database's version before anything else touches it. An unversioned database (0,
    /// which reads as 1) is stamped with <paramref name="supported"/>; a newer one throws
    /// <see cref="NewerFormatException"/> having written nothing, so it must be called before any pragma
    /// or schema statement that could change the file.
    /// </summary>
    public static void Claim(SqliteConnection connection, int supported, string store)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var found = (long)command.ExecuteScalar()!;
        if (found > supported)
        {
            throw new NewerFormatException(store, found, supported);
        }

        if (found == 0)
        {
            command.CommandText = $"PRAGMA user_version = {supported};";
            command.ExecuteNonQuery();
        }
    }
}
