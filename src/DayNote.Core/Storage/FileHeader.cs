using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace DayNote.Core.Storage;

/// <summary>One format DayNote owns: the file's kind and the format version this build reads and writes.</summary>
public sealed record FileFormat(string Kind, int Version);

/// <summary>Every format DayNote owns (file-identity-conventions), each at its version in <see cref="FormatVersions"/>.</summary>
public static class FileFormats
{
    public static readonly FileFormat Binder = new("binder", FormatVersions.Binder);
    public static readonly FileFormat Config = new("config", FormatVersions.Config);
    public static readonly FileFormat BinderList = new("binderList", FormatVersions.Binders);
    public static readonly FileFormat State = new("state", FormatVersions.State);
    public static readonly FileFormat Records = new("records", FormatVersions.Records);
    public static readonly FileFormat Backups = new("backups", FormatVersions.Backups);
}

/// <summary>
/// The file identity header (file-identity-conventions): <c>format</c> says what the file is, as owner,
/// kind and version, and <c>writer</c> says which program and release wrote this copy. Every DayNote format
/// is written and checked here, in JSON, TOML and SQLite. A reader decides on <c>format</c> alone: the
/// owner and kind must be exactly this file's, a version above this build's is newer and left untouched,
/// and anything else, including a missing or malformed header, is unreadable. <c>writer</c> is written on
/// every save and never read.
/// </summary>
public static class FileHeader
{
    public const string Owner = "daynote";

    /// <summary>The package that serializes DayNote's files, lowercase as identifiers are.</summary>
    public const string WriterName = "daynote";

    private const string FormatKey = "format";
    private const string WriterKey = "writer";
    private const string TableName = "file_header";

    /// <summary>This build's release version, from the assembly that writes the files.</summary>
    public static string WriterVersion { get; } = typeof(FileHeader).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// The version a header records when it is one of <paramref name="format"/>'s: the owner and kind
    /// match exactly and the version is a positive integer. Null for any other header, or none.
    /// </summary>
    public static long? VersionOf(FileFormat format, string? owner, string? kind, long? version) =>
        owner == Owner && kind == format.Kind && version is >= 1 ? version : null;

    // ----- JSON ---------------------------------------------------------------------------------

    /// <summary>A JSON root holding the header first, ready for the content's members.</summary>
    public static JsonObject JsonRoot(FileFormat format) => new()
    {
        [FormatKey] = new JsonObject { ["owner"] = Owner, ["kind"] = format.Kind, ["version"] = format.Version },
        [WriterKey] = new JsonObject { ["name"] = WriterName, ["version"] = WriterVersion },
    };

    /// <summary>
    /// The version a JSON root's header records for <paramref name="format"/>, or null when it has no
    /// such header. The header's members are removed, leaving the content.
    /// </summary>
    public static long? TakeJson(JsonObject root, FileFormat format)
    {
        var header = root[FormatKey] as JsonObject;
        root.Remove(FormatKey);
        root.Remove(WriterKey);
        return header is null
            ? null
            : VersionOf(format, Text(header["owner"]), Text(header["kind"]), Integer(header["version"]));

        static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        static long? Integer(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;
    }

    // ----- TOML ---------------------------------------------------------------------------------

    /// <summary>The header as the first two lines of a TOML file, two inline tables.</summary>
    public static string TomlLines(FileFormat format) =>
        $"format = {{ owner = \"{Owner}\", kind = \"{format.Kind}\", version = {format.Version} }}\n" +
        $"writer = {{ name = \"{WriterName}\", version = \"{WriterVersion}\" }}\n";

    // ----- SQLite -------------------------------------------------------------------------------

    /// <summary>
    /// Checks an open database's header before anything else touches it, once, when the store opens for
    /// writing: the single-instance lease leaves no other writer that could change it during use. A
    /// brand-new, empty database gets the header. Otherwise <c>PRAGMA user_version</c> above this build's is
    /// newer (<see cref="NewerFormatException"/>), and a version that is not this build's, or a
    /// <c>file_header</c> table that is missing or names another owner or kind, is unreadable
    /// (<see cref="InvalidDataException"/>); either way nothing is written. A database that passes has its
    /// writer rows rewritten. This must run before any pragma or schema statement that could change the file.
    /// </summary>
    public static void ClaimSqlite(SqliteConnection connection, FileFormat format, string store)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var found = (long)command.ExecuteScalar()!;
        if (found > format.Version)
        {
            throw new NewerFormatException(store, found, format.Version);
        }

        if (found == 0)
        {
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM sqlite_master);";
            if ((long)command.ExecuteScalar()! != 0)
            {
                throw new InvalidDataException($"{store} records no format version.");
            }

            using var create = connection.BeginTransaction();
            command.Transaction = create;
            command.CommandText = $"CREATE TABLE {TableName} (key TEXT PRIMARY KEY, value TEXT NOT NULL);";
            command.ExecuteNonQuery();
            WriteRows(command, ("format.owner", Owner), ("format.kind", format.Kind));
            WriteWriterRows(command);
            command.CommandText = $"PRAGMA user_version = {format.Version};";
            command.ExecuteNonQuery();
            create.Commit();
            return;
        }

        command.CommandText = $"SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{TableName}');";
        var hasHeader = (long)command.ExecuteScalar()! != 0;
        string? owner = null, kind = null;
        if (hasHeader)
        {
            command.CommandText = $"SELECT key, value FROM {TableName} WHERE key IN ('format.owner', 'format.kind');";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(0) == "format.owner")
                    owner = reader.GetString(1);
                else
                    kind = reader.GetString(1);
            }
        }

        if (VersionOf(format, owner, kind, found) != format.Version)
        {
            throw new InvalidDataException($"{store} is not a {Owner} {format.Kind} database this build reads.");
        }

        using var rewrite = connection.BeginTransaction();
        command.Transaction = rewrite;
        command.CommandText = $"DELETE FROM {TableName} WHERE key LIKE 'writer.%';";
        command.ExecuteNonQuery();
        WriteWriterRows(command);
        rewrite.Commit();
    }

    private static void WriteWriterRows(SqliteCommand command) =>
        WriteRows(command, ("writer.name", WriterName), ("writer.version", WriterVersion));

    private static void WriteRows(SqliteCommand command, params (string Key, string Value)[] rows)
    {
        command.CommandText = $"INSERT OR REPLACE INTO {TableName} (key, value) VALUES ($key, $value);";
        foreach (var (key, value) in rows)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        command.Parameters.Clear();
    }
}
