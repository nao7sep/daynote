using System.Globalization;

namespace DayNote.Core.Time;

/// <summary>
/// Timestamp conventions for DayNote. Internal timestamps are UTC, ISO-8601 with millisecond
/// precision (the serialized form used for data values such as the backup store's written_at_utc).
/// Filename timestamps use <c>yyyymmdd-hhmmss-fff-utc</c> at millisecond precision — currently the
/// per-launch log filename — so two events within the same second still produce distinct names.
/// User-facing timestamps are rendered in the display zone: the computer's own by default
/// (<see cref="SystemZone"/>), or a zone the user chose from <see cref="ZoneIds"/>.
/// </summary>
public static class DayNoteTime
{
    private const string IsoFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    private static readonly string[] AcceptedIsoFormats =
    {
        "yyyy-MM-ddTHH:mm:ss.fffZ",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.fffzzz",
        "yyyy-MM-ddTHH:mm:sszzz",
    };

    /// <summary>Formats a timestamp as a quoted-string-ready ISO-8601 UTC value, e.g. <c>2026-06-03T14:23:05.482Z</c>.</summary>
    public static string ToIso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(IsoFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses an ISO-8601 timestamp leniently, so hand-edited files round-trip. Falls back to a
    /// general parse when the value does not match the canonical formats.
    /// </summary>
    public static DateTimeOffset ParseIso(string text) =>
        TryParseIso(text, out var value)
            ? value
            : throw new FormatException($"Not a recognized ISO-8601 timestamp: '{text}'");

    /// <summary>
    /// Attempts to parse an ISO-8601 timestamp leniently. Returns false instead of throwing, so
    /// callers loading hand-edited files can fall back rather than failing the whole load.
    /// </summary>
    public static bool TryParseIso(string text, out DateTimeOffset value)
    {
        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

        return DateTimeOffset.TryParseExact(text, AcceptedIsoFormats, CultureInfo.InvariantCulture, styles, out value)
            || DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, styles, out value);
    }

    /// <summary>Filename-safe UTC stamp in the <c>yyyymmdd-hhmmss-fff-utc</c> convention (millisecond precision).</summary>
    public static string FileStamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-utc";

    /// <summary>The saved time-zone setting that means "follow the computer".</summary>
    public const string SystemZone = "system";

    /// <summary>
    /// The zone a saved setting displays times in: the computer's own for <see cref="SystemZone"/>,
    /// read afresh so a computer taken to another zone shows local times at its next launch, and for a
    /// missing or unknown id, so a hand-edited file never leaves the app without a zone. The computer's
    /// zone is UTC when the platform cannot say.
    /// </summary>
    public static TimeZoneInfo DisplayZone(string? setting) =>
        !IsSystem(setting) && TryResolveTimeZone(setting!.Trim(), out var zone) ? zone : TimeZoneInfo.Local;

    /// <summary>Whether a saved setting follows the computer's zone rather than naming one.</summary>
    public static bool IsSystem(string? setting) =>
        string.IsNullOrWhiteSpace(setting) || string.Equals(setting.Trim(), SystemZone, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The IANA id of the computer's zone, as the list names it: macOS reports IANA ids already, and a
    /// Windows zone id is converted to its IANA equivalent.
    /// </summary>
    public static string SystemZoneId() => IanaIdOf(TimeZoneInfo.Local) ?? TimeZoneInfo.Local.Id;

    /// <summary>
    /// The zones a user chooses from after System: every zone the platform knows, by IANA id, plus UTC
    /// and <paramref name="saved"/> when the platform's list lacks them, so a stored choice always stays
    /// selectable. Sorted by id.
    /// </summary>
    public static IReadOnlyList<string> ZoneIds(string? saved = null)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal) { "UTC" };
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            if (IanaIdOf(zone) is { } id)
            {
                ids.Add(id);
            }
        }

        if (!IsSystem(saved) && TryResolveTimeZone(saved!.Trim(), out _))
        {
            ids.Add(saved.Trim());
        }

        return ids.ToList();
    }

    private static string? IanaIdOf(TimeZoneInfo zone)
    {
        if (zone.HasIanaId)
        {
            return zone.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null;
    }

    /// <summary>
    /// Renders a UTC timestamp for display in <paramref name="zone"/>, in the ISO-like format
    /// <c>yyyy-MM-dd HH:mm:ss</c>.
    /// </summary>
    public static string ToDisplay(DateTimeOffset value, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(value.ToUniversalTime(), zone);
        return local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Renders a UTC timestamp for the status bar, relative to <paramref name="now"/>, in
    /// <paramref name="zone"/>: time only (<c>HH:mm</c>) when it falls on the same calendar day,
    /// abbreviated month and day (<c>MMM d</c>) when within the same year, otherwise the full date
    /// (<c>yyyy-MM-dd</c>). Both the same-day/same-year comparison and the formatting run in the zone
    /// with the invariant culture, so the result is identical regardless of the host's system locale.
    /// </summary>
    public static string ToSmartDisplay(DateTimeOffset value, TimeZoneInfo zone, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(value.ToUniversalTime(), zone);
        var localNow = TimeZoneInfo.ConvertTime(now.ToUniversalTime(), zone);

        if (local.Date == localNow.Date)
        {
            return local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (local.Year == localNow.Year)
        {
            return local.ToString("MMM d", CultureInfo.InvariantCulture);
        }

        return local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Attempts to resolve an IANA/Windows time-zone id to a system time zone. Returns false for null,
    /// blank, unknown, or malformed ids (with <paramref name="zone"/> set to UTC) instead of throwing,
    /// so callers can both validate user input and fall back to UTC from a single code path.
    /// </summary>
    public static bool TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo zone)
    {
        // A null/blank id (e.g. a hand-edited "timeZone": null in config.json) is not a valid
        // zone and must never reach FindSystemTimeZoneById, which throws ArgumentNullException on null —
        // treat it the same as an unknown id and fall back to UTC.
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
