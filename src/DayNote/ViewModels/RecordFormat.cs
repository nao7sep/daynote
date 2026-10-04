using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayNote.Core.Time;
using DayNote.I18n;
using DayNote.Logging;

namespace DayNote.ViewModels;

/// <summary>How the records window words a record's stored values.</summary>
public static class RecordFormat
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The level filter's choices in the order it offers them.</summary>
    public static readonly RecordLevelFilter[] LevelFilters =
        [RecordLevelFilter.Attention, RecordLevelFilter.Error, RecordLevelFilter.Warn, RecordLevelFilter.Info, RecordLevelFilter.Debug];

    public static string LevelFilterKey(RecordLevelFilter level) => level switch
    {
        RecordLevelFilter.Attention => "records.levelAttention",
        RecordLevelFilter.Error => "records.levelError",
        RecordLevelFilter.Warn => "records.levelWarn",
        RecordLevelFilter.Info => "records.levelInfo",
        RecordLevelFilter.Debug => "records.levelDebug",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    /// <summary>A stored level in the interface language; a level the app does not write shows as stored.</summary>
    public static string LevelText(string level) => level switch
    {
        "error" => Localizer.T("records.levelError"),
        "warn" => Localizer.T("records.levelWarn"),
        "info" => Localizer.T("records.levelInfo"),
        "debug" => Localizer.T("records.levelDebug"),
        _ => level,
    };

    /// <summary>A stored time in the display zone, to the second or, for the detail, the millisecond.</summary>
    public static string TimeText(string time, TimeZoneInfo zone, bool milliseconds = false) =>
        DayNoteTime.TryParseIso(time, out var value)
            ? DayNoteTime.ToPreciseDisplay(value, zone, Localizer.Current.Culture, milliseconds)
            : time;

    /// <summary>A launch by its session, marked when it is this one.</summary>
    public static string LaunchText(string session, string currentSession, TimeZoneInfo zone)
    {
        var time = TimeText(session, zone);
        return session == currentSession ? Localizer.T("records.thisLaunch", ("time", time)) : time;
    }

    /// <summary>
    /// A record's stored fields as the Details and Error blocks show them, each indented for reading
    /// and null when it has nothing to show. The exception the logger attaches under <c>error</c> is
    /// its own block; Details leaves it out, and leaves out the note id the Note field already shows.
    /// Text that is not JSON is shown in Details as it is.
    /// </summary>
    public static (string? Details, string? Error) SplitFields(string fields, string? noteId)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(fields);
        }
        catch (JsonException)
        {
            return (string.IsNullOrWhiteSpace(fields) ? null : fields, null);
        }

        JsonNode? error = null;
        if (node is JsonObject shown)
        {
            if (noteId is not null
                && shown["noteId"] is JsonValue id && id.TryGetValue<string>(out var stored) && stored == noteId)
            {
                shown.Remove("noteId");
            }

            if (shown.Remove("error", out var attached))
            {
                error = attached;
            }
        }

        return (Block(node), Block(error));
    }

    private static string? Block(JsonNode? node) => node switch
    {
        null or JsonObject { Count: 0 } or JsonArray { Count: 0 } => null,
        JsonValue text when text.TryGetValue<string>(out var words) && string.IsNullOrWhiteSpace(words) => null,
        _ => node.ToJsonString(Indented),
    };
}
