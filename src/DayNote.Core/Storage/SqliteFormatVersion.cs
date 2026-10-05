using Microsoft.Data.Sqlite;

namespace DayNote.Core.Storage;

/// <summary>
/// A SQLite store's format version, kept in <c>PRAGMA user_version</c> (store-recovery-conventions).
/// </summary>
public static class SqliteFormatVersion
{
    /// <summary>
    /// Checks the open database's version before anything else touches it. A brand-new, empty database is
    /// stamped with <paramref name="supported"/>. One holding tables but no version (0) is unreadable and
    /// throws <see cref="InvalidDataException"/>; a newer one throws <see cref="NewerFormatException"/>.
    /// Either way nothing is written, so this must run before any pragma or schema statement that could
    /// change the file.
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
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM sqlite_master);";
            if ((long)command.ExecuteScalar()! != 0)
            {
                throw new InvalidDataException($"{store} records no format version.");
            }

            command.CommandText = $"PRAGMA user_version = {supported};";
            command.ExecuteNonQuery();
        }
    }
}
