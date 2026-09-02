using System;
using System.IO;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class FailurePresentationTests
{
    private const string Hostile = "EACCES Error invoking remote method IPC /private/tmp/hostile-sentinel";

    [Fact]
    public void ArbitraryDiagnosticsNeverBecomeBinderPresentation()
    {
        var error = new IOException(Hostile, new InvalidOperationException("root cause"));

        var open = FailurePresentation.OpenBinder(error);
        var save = FailurePresentation.SaveBinder(error);
        var startup = FailurePresentation.StartupData();
        var recovery = FailurePresentation.RecoveredData(binderListWasReset: true);

        Assert.DoesNotContain(Hostile, startup, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, recovery, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, open, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, save, StringComparison.Ordinal);
        Assert.Contains("could not be opened", open, StringComparison.Ordinal);
        Assert.Contains("changes are still in DayNote", save, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void KnownStructuredFailuresSelectUsefulRecovery()
    {
        Assert.Contains("permission", FailurePresentation.OpenBinder(new UnauthorizedAccessException(Hostile)), StringComparison.Ordinal);
        Assert.Contains("no longer available", FailurePresentation.OpenBinder(new FileNotFoundException(Hostile)), StringComparison.Ordinal);
        Assert.Contains("writable", FailurePresentation.SaveBinder(new UnauthorizedAccessException(Hostile)), StringComparison.Ordinal);
    }
}
