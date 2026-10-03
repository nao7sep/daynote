using Microsoft.Data.Sqlite;

namespace DayNote.Logging;

/// <summary>
/// The records window's queries against the <c>logs</c> table: a filtered page newest first, keyset
/// paged by (time, id), one record whole, and the sessions that have records. Pure reads over the
/// connection they are handed.
/// </summary>
internal static class RecordsReads
{
    internal const int PageSize = 100;

    // The columns a search looks in: the message, the note, and every field given.
    private static readonly string[] Searched = ["message", "note_id", "fields"];

    internal static RecordsPage Page(SqliteConnection connection, RecordsQuery query)
    {
        using var command = connection.CreateCommand();
        var where = new List<string>();

        if (query.Session is { } session)
        {
            where.Add("session = $session");
            command.Parameters.AddWithValue("$session", session);
        }

        switch (query.Level)
        {
            case RecordLevelFilter.Attention:
                where.Add("level IN ('warn', 'error')");
                break;
            case { } level:
                where.Add("level = $level");
                command.Parameters.AddWithValue("$level", LevelName(level));
                break;
        }

        if (LikePattern(query.Search) is { } pattern)
        {
            where.Add("(" + string.Join(" OR ", Searched.Select(column => $"{column} LIKE $search ESCAPE '\\'")) + ")");
            command.Parameters.AddWithValue("$search", pattern);
        }

        if (query.After is { } after)
        {
            where.Add("(time < $afterTime OR (time = $afterTime AND id < $afterId))");
            command.Parameters.AddWithValue("$afterTime", after.Time);
            command.Parameters.AddWithValue("$afterId", after.Id);
        }

        command.CommandText =
            "SELECT id, time, session, level, message, note_id FROM logs" +
            (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") +
            " ORDER BY time DESC, id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", PageSize + 1);

        var rows = new List<RecordSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RecordSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        var more = rows.Count > PageSize;
        if (more)
        {
            rows.RemoveAt(PageSize);
        }

        return new RecordsPage(rows, more);
    }

    internal static RecordDetail? Detail(SqliteConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, time, session, level, message, note_id, fields FROM logs WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new RecordDetail(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6));
    }

    internal static IReadOnlyList<string> Sessions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT session FROM logs ORDER BY session DESC";
        var sessions = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(reader.GetString(0));
        }

        return sessions;
    }

    /// <summary>The level as the logger stores it.</summary>
    internal static string LevelName(RecordLevelFilter level) => level switch
    {
        RecordLevelFilter.Error => "error",
        RecordLevelFilter.Warn => "warn",
        RecordLevelFilter.Info => "info",
        RecordLevelFilter.Debug => "debug",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    // The search as a LIKE pattern matching it anywhere, its own wildcards taken literally.
    private static string? LikePattern(string search)
    {
        var trimmed = search.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var escaped = trimmed.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return "%" + escaped + "%";
    }
}
