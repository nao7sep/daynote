using System.Globalization;

namespace DayNote.Core.Time;

/// <summary>
/// Timestamp conventions for DayNote. Internal timestamps are UTC, ISO-8601 with millisecond
/// precision. Filename timestamps use <c>yyyymmdd-hhmmss-utc</c>. User-facing timestamps are
/// rendered in a configurable time zone (default Asia/Tokyo) in an ISO-like format.
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

    /// <summary>Filename-safe UTC stamp in the <c>yyyymmdd-hhmmss-utc</c> convention.</summary>
    public static string FileStamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-utc";

    /// <summary>
    /// Renders a UTC timestamp for display in the given IANA time zone (e.g. <c>Asia/Tokyo</c>),
    /// in the ISO-like format <c>yyyy-MM-dd HH:mm:ss</c>. Falls back to UTC if the zone is unknown.
    /// </summary>
    public static string ToDisplay(DateTimeOffset value, string timeZoneId)
    {
        var zone = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(value.ToUniversalTime(), zone);
        return local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
