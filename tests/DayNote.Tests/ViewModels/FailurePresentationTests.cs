using System;
using System.IO;
using DayNote.Tests.I18n;
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

        var open = English.Of(FailurePresentation.OpenBinder(error));
        var save = English.Of(FailurePresentation.SaveBinder(error));
        var newBinderPicker = English.Of(FailurePresentation.NewBinderPicker(error));
        var openBinderPicker = English.Of(FailurePresentation.OpenBinderPicker(error));
        var attachmentPicker = English.Of(FailurePresentation.AttachmentPicker(error));
        var reload = English.Of(FailurePresentation.ReloadBinder(error));
        var link = English.Of(FailurePresentation.OpenExternalLink(error));
        var startup = English.Of(FailurePresentation.StartupData());
        var startupStorage = English.Of(FailurePresentation.StartupStorage());
        var recovery = English.Of(FailurePresentation.RecoveredData(binderListWasReset: true));

        Assert.DoesNotContain(Hostile, startup, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, startupStorage, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, recovery, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, open, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, save, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, newBinderPicker, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, openBinderPicker, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, attachmentPicker, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, reload, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, link, StringComparison.Ordinal);
        Assert.Contains("could not be opened", open, StringComparison.Ordinal);
        Assert.Contains("changes are still in DayNote", save, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void KnownStructuredFailuresSelectUsefulRecovery()
    {
        Assert.Contains("permission", English.Of(FailurePresentation.OpenBinder(new UnauthorizedAccessException(Hostile))), StringComparison.Ordinal);
        Assert.Contains("no longer available", English.Of(FailurePresentation.OpenBinder(new FileNotFoundException(Hostile))), StringComparison.Ordinal);
        Assert.Contains("writable", English.Of(FailurePresentation.SaveBinder(new UnauthorizedAccessException(Hostile))), StringComparison.Ordinal);
    }
}
