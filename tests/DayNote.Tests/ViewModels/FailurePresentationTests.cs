using System;
using System.IO;
using DayNote.Core.Storage;
using DayNote.Core.Toml;
using DayNote.Tests.I18n;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class FailurePresentationTests
{
    private const string Hostile = "EACCES Error invoking remote method IPC /private/tmp/hostile-sentinel";

    [Fact]
    public void The_load_failed_notice_names_the_settings_file_and_says_it_was_left_in_place()
    {
        var config = Path.Combine(Path.GetTempPath(), "daynote-home", "config.json");

        var text = English.Of(FailurePresentation.StartupSettings(config, new IOException(Hostile)));

        Assert.Equal("failure.startupSettings", FailurePresentation.StartupSettings(config, new IOException(Hostile)).Key);
        Assert.Contains(config, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_settings_file_is_named_left_in_place_and_says_what_to_do()
    {
        var config = Path.Combine(Path.GetTempPath(), "daynote-home", "config.json");

        var text = English.Of(FailurePresentation.StartupSettings(config, new InvalidDataException(Hostile)));

        Assert.Equal("failure.startupSettingsMalformed", FailurePresentation.StartupSettings(config, new InvalidDataException(Hostile)).Key);
        Assert.Contains(config, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ArbitraryDiagnosticsNeverBecomeBinderPresentation()
    {
        var error = new IOException(Hostile, new InvalidOperationException("root cause"));

        var open = English.Of(FailurePresentation.OpenBinder(error, "Journal"));
        var save = English.Of(FailurePresentation.SaveBinder(error));
        var newBinderPicker = English.Of(FailurePresentation.NewBinderPicker(error));
        var openBinderPicker = English.Of(FailurePresentation.OpenBinderPicker(error));
        var attachmentPicker = English.Of(FailurePresentation.AttachmentPicker(error));
        var reload = English.Of(FailurePresentation.ReloadBinder(error));
        var link = English.Of(FailurePresentation.OpenExternalLink(error));
        var startup = English.Of(FailurePresentation.StartupSettings("config.json", error));
        var startupStorage = English.Of(FailurePresentation.StartupStorage());

        Assert.DoesNotContain(Hostile, startup, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, startupStorage, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, open, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, save, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, newBinderPicker, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, openBinderPicker, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, attachmentPicker, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, reload, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, link, StringComparison.Ordinal);
        Assert.Equal("failure.openBinder", FailurePresentation.OpenBinder(error, "Journal").Key);
        Assert.Equal("failure.saveBinder", FailurePresentation.SaveBinder(error).Key);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void KnownStructuredFailuresSelectUsefulRecovery()
    {
        Assert.Equal("failure.openBinderPermission", FailurePresentation.OpenBinder(new UnauthorizedAccessException(Hostile), "Journal").Key);
        Assert.Equal("failure.openBinderGone", FailurePresentation.OpenBinder(new FileNotFoundException(Hostile), "Journal").Key);
        Assert.Equal("failure.saveBinderPermission", FailurePresentation.SaveBinder(new UnauthorizedAccessException(Hostile)).Key);
    }

    [Fact]
    public void A_malformed_binder_is_named_and_reported_as_left_untouched()
    {
        var message = FailurePresentation.OpenBinder(new BinderFormatException(Hostile), "Journal");
        var text = English.Of(message);

        Assert.Equal("failure.openBinderFormat", message.Key);
        Assert.Contains("Journal", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_binder_is_named_and_reported_as_left_untouched()
    {
        var message = FailurePresentation.OpenBinder(new NewerFormatException(Hostile, 2, 1), "Journal");
        var text = English.Of(message);

        Assert.Equal("failure.openBinderNewer", message.Key);
        Assert.Contains("Journal", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_settings_file_is_named_and_reported_as_left_in_place()
    {
        var config = Path.Combine(Path.GetTempPath(), "daynote-home", "config.json");

        var text = English.Of(FailurePresentation.StartupSettings(config, new NewerFormatException(Hostile, 2, 1)));

        Assert.Equal("failure.startupSettingsNewer", FailurePresentation.StartupSettings(config, new NewerFormatException(Hostile, 2, 1)).Key);
        Assert.Contains(config, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, text, StringComparison.Ordinal);
    }
}
