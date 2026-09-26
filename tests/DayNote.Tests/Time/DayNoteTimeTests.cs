using System;
using System.Globalization;
using System.Linq;
using DayNote.Core.Time;
using Xunit;

namespace DayNote.Tests.Time;

public sealed class DayNoteTimeTests
{
    [Theory]
    [InlineData("Asia/Tokyo")]
    [InlineData("America/New_York")]
    [InlineData("Europe/London")]
    [InlineData("UTC")]
    public void TryResolveTimeZone_returns_true_for_a_known_zone(string id)
    {
        var resolved = DayNoteTime.TryResolveTimeZone(id, out var zone);

        Assert.True(resolved);
        Assert.NotNull(zone);
    }

    [Theory]
    [InlineData("Mars/Phobos")]
    [InlineData("Asia/Atlantis")]
    [InlineData("not-a-zone")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveTimeZone_returns_false_and_falls_back_to_utc_for_an_unknown_zone(string id)
    {
        var resolved = DayNoteTime.TryResolveTimeZone(id, out var zone);

        Assert.False(resolved);
        Assert.Equal(TimeZoneInfo.Utc, zone);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("SYSTEM")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Totally/Bogus")]
    public void DisplayZone_follows_the_computer_for_system_and_for_anything_no_zone_answers_to(string? setting) =>
        Assert.Equal(TimeZoneInfo.Local, DayNoteTime.DisplayZone(setting));

    [Fact]
    public void DisplayZone_resolves_a_chosen_zone() =>
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById("Europe/London"), DayNoteTime.DisplayZone("Europe/London"));

    [Fact]
    public void ZoneIds_lists_iana_zones_sorted_with_utc_and_never_a_windows_id()
    {
        var ids = DayNoteTime.ZoneIds();

        Assert.Contains("UTC", ids);
        Assert.Contains("Asia/Tokyo", ids);
        Assert.Contains("America/New_York", ids);
        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
        Assert.Equal(ids.Distinct(), ids);
        Assert.DoesNotContain("Tokyo Standard Time", ids);
        Assert.All(ids, id => Assert.True(DayNoteTime.TryResolveTimeZone(id, out _), id));
    }

    [Fact]
    public void ZoneIds_keeps_a_saved_zone_selectable() =>
        Assert.Contains("Etc/GMT+5", DayNoteTime.ZoneIds("Etc/GMT+5"));

    [Fact]
    public void ToDisplay_renders_in_the_given_zone()
    {
        var instant = new DateTimeOffset(2026, 6, 11, 14, 23, 5, TimeSpan.Zero);

        Assert.Equal("2026-06-11 23:23:05", DayNoteTime.ToDisplay(instant, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo")));
    }

    private static readonly DateTimeOffset SmartNow = new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToSmartDisplay_shows_time_only_for_the_same_day()
    {
        var value = new DateTimeOffset(2026, 6, 21, 14, 30, 0, TimeSpan.Zero);

        Assert.Equal("14:30", DayNoteTime.ToSmartDisplay(value, TimeZoneInfo.Utc, SmartNow));
    }

    [Fact]
    public void ToSmartDisplay_shows_month_and_day_within_the_same_year()
    {
        var value = new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal("Jun 18", DayNoteTime.ToSmartDisplay(value, TimeZoneInfo.Utc, SmartNow));
    }

    [Fact]
    public void ToSmartDisplay_shows_the_full_date_for_an_earlier_year()
    {
        var value = new DateTimeOffset(2024, 12, 1, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal("2024-12-01", DayNoteTime.ToSmartDisplay(value, TimeZoneInfo.Utc, SmartNow));
    }

    [Fact]
    public void ToSmartDisplay_resolves_the_same_day_in_the_display_zone_not_utc()
    {
        // 16:00Z is already the next calendar day in Tokyo (UTC+9) — the same Tokyo day as 17:00Z "now" —
        // so it renders as the Tokyo wall-clock time, not the UTC day or time.
        var now = new DateTimeOffset(2026, 6, 21, 17, 0, 0, TimeSpan.Zero);
        var value = new DateTimeOffset(2026, 6, 21, 16, 0, 0, TimeSpan.Zero);

        Assert.Equal("01:00", DayNoteTime.ToSmartDisplay(value, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"), now));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("ja-JP")]
    [InlineData("fr-FR")]
    public void ToSmartDisplay_is_identical_regardless_of_system_locale(string culture)
    {
        // The format must not leak the ambient culture: no localized/Hijri month names, no AM/PM, no
        // non-Latin digits. Under any culture the output stays the invariant English/24-hour form.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.Equal(
                "Jun 18",
                DayNoteTime.ToSmartDisplay(new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc, SmartNow));
            Assert.Equal(
                "14:30",
                DayNoteTime.ToSmartDisplay(new DateTimeOffset(2026, 6, 21, 14, 30, 0, TimeSpan.Zero), TimeZoneInfo.Utc, SmartNow));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void TryResolveTimeZone_falls_back_to_utc_for_null_or_blank()
    {
        // A null/blank id (e.g. a hand-edited "displayTimeZone": null) must not reach
        // FindSystemTimeZoneById, which throws ArgumentNullException on null.
        Assert.False(DayNoteTime.TryResolveTimeZone(null, out var fromNull));
        Assert.Equal(TimeZoneInfo.Utc, fromNull);

        Assert.False(DayNoteTime.TryResolveTimeZone("   ", out var fromBlank));
        Assert.Equal(TimeZoneInfo.Utc, fromBlank);
    }

    [Fact]
    public void FileStamp_uses_millisecond_precision()
    {
        // Pins the yyyymmdd-hhmmss-fff-utc convention used by the per-launch log filename.
        var value = new DateTimeOffset(2026, 6, 10, 3, 15, 42, 123, TimeSpan.Zero);
        Assert.Equal("20260610-031542-123-utc", DayNoteTime.FileStamp(value));
    }
}
