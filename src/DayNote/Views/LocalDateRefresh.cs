namespace DayNote.Views;

/// <summary>Refreshes calendar-relative metadata at the next date boundary in the display zone.</summary>
internal sealed class LocalDateRefresh(
    TimeProvider clock,
    Func<TimeZoneInfo> zone,
    Action refresh,
    Action<Action> post) : IDisposable
{
    private ITimer? _timer;
    private long _generation;
    private bool _disposed;

    // Reset on the UI thread: activation and zone changes also invalidate an already posted tick.
    internal void RefreshAndSchedule()
    {
        if (_disposed)
            return;
        _timer?.Dispose();
        var generation = ++_generation;
        refresh();
        if (_disposed || generation != _generation)
            return;
        _timer = clock.CreateTimer(_ => post(() =>
        {
            if (!_disposed && generation == _generation)
                RefreshAndSchedule();
        }), null, UntilNextDate(clock.GetUtcNow(), zone()), Timeout.InfiniteTimeSpan);
    }

    internal static TimeSpan UntilNextDate(DateTimeOffset now, TimeZoneInfo zone)
    {
        var midnight = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1), DateTimeKind.Unspecified);
        // Some zones move their clocks at midnight, or skip a whole calendar date.
        while (zone.IsInvalidTime(midnight))
            midnight = midnight.AddMinutes(1);
        var target = zone.IsAmbiguousTime(midnight)
            ? zone.GetAmbiguousTimeOffsets(midnight)
                .Select(offset => new DateTimeOffset(midnight, offset).ToUniversalTime())
                .Where(instant => instant > now).Min()
            : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, zone));
        return target - now;
    }

    public void Dispose()
    {
        _disposed = true;
        ++_generation;
        _timer?.Dispose();
        _timer = null;
    }
}
