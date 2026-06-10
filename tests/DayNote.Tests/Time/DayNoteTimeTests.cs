using System;
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

    [Fact]
    public void ToDisplay_renders_an_unknown_zone_in_utc()
    {
        // The display formatter shares its resolution path with TryResolveTimeZone, so an unresolvable
        // zone must format identically to an explicit UTC request rather than throwing.
        var instant = new DateTimeOffset(2026, 6, 11, 14, 23, 5, TimeSpan.Zero);

        Assert.Equal(
            DayNoteTime.ToDisplay(instant, "UTC"),
            DayNoteTime.ToDisplay(instant, "Totally/Bogus"));
    }
}
