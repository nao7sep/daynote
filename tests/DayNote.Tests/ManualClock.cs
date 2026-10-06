using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DayNote.Tests;

/// <summary>
/// A clock that stands still until a test moves it, so recorded times never depend on the wall clock.
/// Its timers fire only as <see cref="Advance"/> reaches them, so a timeout runs out exactly when a test
/// says so.
/// </summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];

    public DateTimeOffset Now { get; private set; } = now;

    public void Advance(TimeSpan by)
    {
        Now += by;
        ManualTimer[] due;
        lock (_timers)
        {
            due = _timers.Where(timer => timer.DueAt <= Now).OrderBy(timer => timer.DueAt).ToArray();
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset DueAt { get; private set; } = DateTimeOffset.MaxValue;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._timers)
            {
                clock._timers.Remove(this);
                _period = period;
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    DueAt = clock.Now + dueTime;
                    clock._timers.Add(this);
                }
            }

            return true;
        }

        public void Fire()
        {
            lock (clock._timers)
            {
                clock._timers.Remove(this);
                if (_period != Timeout.InfiniteTimeSpan && _period > TimeSpan.Zero)
                {
                    DueAt += _period;
                    clock._timers.Add(this);
                }
            }

            callback(state);
        }

        public void Dispose()
        {
            lock (clock._timers)
            {
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
