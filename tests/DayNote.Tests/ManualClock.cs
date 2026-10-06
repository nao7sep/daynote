using System;

namespace DayNote.Tests;

/// <summary>A clock that stands still until a test moves it, so recorded times never depend on the wall clock.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = now;

    public void Advance(TimeSpan by) => Now += by;

    public override DateTimeOffset GetUtcNow() => Now;
}
