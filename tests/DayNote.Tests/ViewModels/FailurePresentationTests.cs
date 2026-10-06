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
    public void Only_a_set_aside_config_is_reported()
    {
        var home = Path.Combine(Path.GetTempPath(), "daynote-home");
        var config = Path.Combine(home, "config-20261002-101500-000-utc.invalid");
        var state = Path.Combine(home, "state-20261002-101500-000-utc.invalid");

        Assert.Equal(config, FailurePresentation.SetAsideConfig([state, config], Path.Combine(home, "config.json")));
        Assert.Null(FailurePresentation.SetAsideConfig([state], Path.Combine(home, "config.json")));
    }

    [Fact]
    public void A_set_aside_config_from_another_folder_is_not_this_one()
    {
        var other = Path.Combine(Path.GetTempPath(), "another-home", "config-20261002-101500-000-utc.invalid");

        Assert.Null(FailurePresentation.SetAsideConfig([other], Path.Combine(Path.GetTempPath(), "daynote-home", "config.json")));
    }

    [Fact]
    public void The_load_failed_notice_names_the_settings_file_and_says_it_was_left_in_place()
    {
        var config = Path.Combine(Path.GetTempPath(), "daynote-home", "config.json");

        var text = English.Of(FailurePresentation.StartupSettings(config, new IOException(Hostile)));

        Assert.Contains(config, text, StringComparison.Ordinal);
        Assert.Contains("settings file", text, StringComparison.Ordinal);
        Assert.Contains("left in place", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_settings_reset_notice_names_the_set_aside_copy()
    {
        var copy = Path.Combine(Path.GetTempPath(), "daynote-home", "config-20261002-101500-000-utc.invalid");

        Assert.Contains(copy, English.Of(FailurePresentation.SettingsReset(copy)), StringComparison.Ordinal);
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
        var recovery = English.Of(FailurePresentation.SettingsReset("config.invalid"));

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
        Assert.Contains("permission", English.Of(FailurePresentation.OpenBinder(new UnauthorizedAccessException(Hostile), "Journal")), StringComparison.Ordinal);
        Assert.Contains("no longer available", English.Of(FailurePresentation.OpenBinder(new FileNotFoundException(Hostile), "Journal")), StringComparison.Ordinal);
        Assert.Contains("writable", English.Of(FailurePresentation.SaveBinder(new UnauthorizedAccessException(Hostile))), StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_binder_is_named_and_reported_as_left_untouched()
    {
        var text = English.Of(FailurePresentation.OpenBinder(new BinderFormatException(Hostile), "Journal"));

        Assert.Contains("“Journal”", text, StringComparison.Ordinal);
        Assert.Contains("not valid DayNote data", text, StringComparison.Ordinal);
        Assert.Contains("left untouched", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_binder_is_named_and_reported_as_left_untouched()
    {
        var text = English.Of(FailurePresentation.OpenBinder(new NewerFormatException(Hostile, 2, 1), "Journal"));

        Assert.Contains("“Journal”", text, StringComparison.Ordinal);
        Assert.Contains("newer version of DayNote", text, StringComparison.Ordinal);
        Assert.Contains("left untouched", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_settings_file_is_named_and_reported_as_left_in_place()
    {
        var config = Path.Combine(Path.GetTempPath(), "daynote-home", "config.json");

        var text = English.Of(FailurePresentation.StartupSettings(config, new NewerFormatException(Hostile, 2, 1)));

        Assert.Contains(config, text, StringComparison.Ordinal);
        Assert.Contains("newer version of DayNote", text, StringComparison.Ordinal);
        Assert.Contains("left in place", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Hostile, text, StringComparison.Ordinal);
    }
}
