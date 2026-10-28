using System;
using System.Collections.Generic;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class LocalDateRefreshTests
{
    [Fact]
    public void Midnight_and_new_year_refresh_then_schedule_the_following_date()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.Zero));
        var dates = new List<DateTime>();
        using var owner = new LocalDateRefresh(clock, () => TimeZoneInfo.Utc,
            () => dates.Add(clock.Now.UtcDateTime.Date), action => action());
        owner.RefreshAndSchedule();

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new[] { new DateTime(2026, 12, 31), new DateTime(2027, 1, 1) }, dates);
        clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal(2, dates.Count);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(new DateTime(2027, 1, 2), dates[2]);
    }

    [Fact]
    public void Changing_the_zone_refreshes_immediately_and_replaces_the_old_boundary()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 6, 1, 20, 0, 0, TimeSpan.Zero));
        var zone = TimeZoneInfo.Utc;
        var calls = 0;
        using var owner = new LocalDateRefresh(clock, () => zone, () => calls++, action => action());
        owner.RefreshAndSchedule();
        zone = TimeZoneInfo.CreateCustomTimeZone("ahead", TimeSpan.FromHours(3), "ahead", "ahead");
        owner.RefreshAndSchedule();
        Assert.Equal(2, calls);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(3, calls);
        clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(3, calls); // The original UTC midnight timer was disposed.
    }

    [Fact]
    public void A_posted_tick_does_nothing_after_disposal()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 6, 1, 23, 59, 0, TimeSpan.Zero));
        var posted = new Queue<Action>();
        var calls = 0;
        using var owner = new LocalDateRefresh(clock, () => TimeZoneInfo.Utc, () => calls++, posted.Enqueue);
        owner.RefreshAndSchedule();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(posted);
        owner.Dispose();

        posted.Dequeue()();
        Assert.Equal(1, calls);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Empty(posted);
    }

    [Fact]
    public void A_tick_posted_before_a_reset_leaves_one_boundary_armed()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 6, 1, 23, 59, 0, TimeSpan.Zero));
        var posted = new Queue<Action>();
        var calls = 0;
        using var owner = new LocalDateRefresh(clock, () => TimeZoneInfo.Utc, () => calls++, posted.Enqueue);
        owner.RefreshAndSchedule();
        clock.Advance(TimeSpan.FromMinutes(1));
        owner.RefreshAndSchedule();

        posted.Dequeue()();
        Assert.Equal(3, calls);
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Single(posted);
    }

    [Fact]
    public void An_invalid_midnight_schedules_the_first_valid_minute_of_the_new_date()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 1);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("midnight-jump", TimeSpan.Zero,
            "midnight-jump", "standard", "summer", [rule]);
        var now = new DateTimeOffset(2026, 2, 28, 23, 30, 0, TimeSpan.Zero);

        Assert.True(zone.IsInvalidTime(new DateTime(2026, 3, 1, 0, 0, 0)));
        Assert.Equal(TimeSpan.FromMinutes(30), LocalDateRefresh.UntilNextDate(now, zone));
    }

    [Fact]
    public void An_ambiguous_midnight_uses_the_earliest_upcoming_utc_instant()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 1);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("midnight-repeat", TimeSpan.Zero,
            "midnight-repeat", "standard", "summer", [rule]);
        var now = new DateTimeOffset(2026, 9, 30, 22, 30, 0, TimeSpan.Zero);

        Assert.True(zone.IsAmbiguousTime(new DateTime(2026, 10, 1, 0, 0, 0)));
        Assert.Equal(TimeSpan.FromMinutes(30), LocalDateRefresh.UntilNextDate(now, zone));
    }
}
