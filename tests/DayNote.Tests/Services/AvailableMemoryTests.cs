using System;
using DayNote.Services;
using Xunit;

namespace DayNote.Tests.Services;

public sealed class AvailableMemoryTests
{
    [Fact]
    public void An_operation_may_take_half_of_what_the_system_can_give_and_no_figure_means_no_estimate()
    {
        Assert.True(AvailableMemory.Fits(500, 1000));
        Assert.False(AvailableMemory.Fits(501, 1000));
        Assert.True(AvailableMemory.Fits(long.MaxValue, null));
    }

    [MacOnlyFact]
    public void On_macOS_the_figure_counts_reclaimable_memory_and_stays_within_the_machine()
    {
        var available = AvailableMemory.Bytes();

        Assert.NotNull(available);
        Assert.InRange(available!.Value, 1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
    }

    [WindowsOnlyFact]
    public void On_Windows_the_figure_is_the_available_physical_memory()
    {
        var available = AvailableMemory.Bytes();

        Assert.NotNull(available);
        Assert.InRange(available!.Value, 1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
    }
}
