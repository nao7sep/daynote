namespace DayNote.Logging;

/// <summary>
/// What the level filter offers: one stored level, or <see cref="Attention"/>, every record at
/// <c>warn</c> or <c>error</c>.
/// </summary>
public enum RecordLevelFilter
{
    Attention,
    Error,
    Warn,
    Info,
    Debug,
}

/// <summary>Where the next page starts: the last record of the page before it.</summary>
public sealed record RecordCursor(string Time, long Id);

/// <summary>
/// One page's filters. Null <paramref name="Session"/> or <paramref name="Level"/> means all; a blank
/// <paramref name="Search"/> matches everything.
/// </summary>
public sealed record RecordsQuery(string? Session, RecordLevelFilter? Level, string Search, RecordCursor? After);

/// <summary>A record as the list shows it: everything but its fields.</summary>
public sealed record RecordSummary(long Id, string Time, string Session, string Level, string Message, string? NoteId);

/// <summary>A page of records, newest first, and whether more follow it.</summary>
public sealed record RecordsPage(IReadOnlyList<RecordSummary> Records, bool More);

/// <summary>One record whole, every column as the database holds it; <c>Fields</c> is JSON text.</summary>
public sealed record RecordDetail(
    long Id, string Time, string Session, string Level, string Message, string? NoteId, string Fields);

/// <summary>
/// What the records window reads from <c>records.sqlite3</c>. Each read runs off the caller's thread,
/// within a bound, and writes no record itself.
/// </summary>
public interface IRecordsSource
{
    /// <summary>This launch's session, as every record of it carries.</summary>
    string Session { get; }

    Task<RecordsPage> ReadPageAsync(RecordsQuery query);

    /// <summary>The record, or null when no record has that id.</summary>
    Task<RecordDetail?> ReadDetailAsync(long id);

    /// <summary>Every session that has records, newest first.</summary>
    Task<IReadOnlyList<string>> ReadSessionsAsync();

    /// <summary>
    /// Raised, on the logger's own thread, after each entry the database stored. An entry that went
    /// to the fallback file is not in the database and raises nothing.
    /// </summary>
    event Action? Stored;
}
