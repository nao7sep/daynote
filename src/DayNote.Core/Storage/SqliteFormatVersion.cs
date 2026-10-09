using Microsoft.Data.Sqlite;

namespace DayNote.Core.Storage;

/// <summary>
/// A SQLite store's format version, kept in <c>PRAGMA user_version</c> (store-recovery-conventions).
/// </summary>
public static class SqliteFormatVersion
{
    /// <summary>
    /// Checks the open database's version before anything else touches it, once, when the store opens:
    /// the single-instance lease leaves no other writer that could change it during use. A brand-new,
    /// empty database is stamped with <paramref name="supported"/>. One holding tables but no version (0),
    /// or a version below 0, is not a store this build wrote and throws <see cref="InvalidDataException"/>;
    /// a newer one throws <see cref="NewerFormatException"/>. Either way nothing is written, so this must
    /// run before any pragma or schema statement that could change the file.
    /// </summary>
    /// <returns>The version the store recorded before this call: 0 for a new database the call stamped,
    /// otherwise a version from 1 to <paramref name="supported"/>, which an older one the caller converts.</returns>
    public static long Claim(SqliteConnection connection, int supported, string store)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var found = (long)command.ExecuteScalar()!;
        if (found > supported)
        {
            throw new NewerFormatException(store, found, supported);
        }

        if (found < 0)
        {
            throw new InvalidDataException($"{store} records format version {found}, which no DayNote writes.");
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

        return found;
    }
}
