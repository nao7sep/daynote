using System.Collections.Generic;
using System.IO;
using DayNote.Core.Storage;
using DayNote.Core.Identity;
using DayNote.I18n;
using Xunit;
using static DayNote.Views.ObjC;

namespace DayNote.Tests.I18n;

/// <summary>
/// The language uses the validated config read before the app is built. Its System fallback leaves
/// the same original failure available to the required startup gate.
/// </summary>
public sealed class LanguageBootstrapTests : System.IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daynote-bootstrap-" + IdGenerator.New());

    public LanguageBootstrapTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("""{ "formatVersion": 1, "language": "ja" }""", "ja")]
    [InlineData("""{ "formatVersion": 1, "language": "pt-br", "theme": "dark" }""", "pt-BR")]
    [InlineData("""{ "formatVersion": 1, "language": "JA" }""", "ja")]
    [InlineData("""{ "formatVersion": 1, "language": "Ja" }""", "ja")]
    [InlineData("""{ "formatVersion": 1, "language": "klingon" }""", Languages.System)]
    [InlineData("""{ "formatVersion": 1, "language": 3 }""", Languages.System)]
    [InlineData("""{ "formatVersion": 1, "theme": "dark" }""", Languages.System)]
    [InlineData("""{ "language": "ja" }""", Languages.System)]
    [InlineData("""{ "formatVersion": 2, "language": "ja" }""", Languages.System)]
    [InlineData("""{ "formatVersion": "1", "language": "ja" }""", Languages.System)]
    [InlineData("""[ "ja" ]""", Languages.System)]
    [InlineData("""{ "language": """, Languages.System)]
    public void the_saved_preference_is_read_forgivingly(string json, string expected)
    {
        var config = Path.Combine(_directory, "config.json");
        File.WriteAllText(config, json);

        Assert.Equal(expected, SavedPreference(config));
    }

    [Theory]
    [InlineData("JA")]
    [InlineData("Ja")]
    [InlineData(" zh-hans ")]
    [InlineData("SYSTEM")]
    [InlineData("xx")]
    public void launch_and_the_settings_read_a_stored_language_the_same_way(string stored)
    {
        var config = Path.Combine(_directory, "config.json");
        File.WriteAllText(config, $$"""{ "formatVersion": 1, "language": "{{stored}}" }""");

        var settings = new DayNote.Core.Storage.ConfigStore(config, _ => { }).Load();

        Assert.Equal(settings.Language, SavedPreference(config));
    }

    [Fact]
    public void no_file_or_no_storage_means_system()
    {
        Assert.Equal(Languages.System, SavedPreference(Path.Combine(_directory, "missing.json")));
        Assert.Equal(Languages.System, SavedPreference(null));
    }

    [Fact]
    public void the_setting_the_app_writes_is_the_one_the_bootstrap_reads()
    {
        var config = Path.Combine(_directory, "config.json");
        new DayNote.Core.Storage.JsonStore<DayNote.Core.Configuration.AppConfig>(
            config, DayNote.Core.Storage.FormatVersions.Config, recordBackup: false).Save(
            new DayNote.Core.Configuration.AppConfig { Language = "ko" });

        Assert.Equal("ko", SavedPreference(config));
    }

    [Fact]
    public void bootstrap_and_startup_share_the_first_validated_config_without_a_second_read()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """{ "formatVersion": 1, "language": "ja" }""");
        var store = new ConfigStore(path, _ => { });

        Assert.Equal("ja", LanguageBootstrap.ReadPreference(store));
        var first = store.Load();
        File.WriteAllText(path, """{ "formatVersion": 1, "language": "ko" }""");

        Assert.Same(first, store.Load());
        Assert.Equal("ja", store.Load().Language);
        // A different lifetime reads its own file; there is no global path cache.
        Assert.Equal("ko", new ConfigStore(path, _ => { }).Load().Language);
    }

    [Theory]
    [InlineData("""{ "formatVersion": 2, "language": "ja" }""")]
    [InlineData("""{ "formatVersion": 1, "language": "ja", "language": "ko" }""")]
    [InlineData("""{ "formatVersion": 1, "language": "ja" """)]
    public void a_bootstrap_failure_remains_the_exact_startup_failure_even_if_the_file_changes(string content)
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, content);
        var before = File.ReadAllBytes(path);
        var store = new ConfigStore(path, _ => { });

        Assert.Equal(Languages.System, LanguageBootstrap.ReadPreference(store));
        Assert.Equal(before, File.ReadAllBytes(path));
        var first = Record.Exception(() => store.Load());
        Assert.NotNull(first);
        File.WriteAllText(path, """{ "formatVersion": 1, "language": "ko" }""");

        Assert.Same(first, Record.Exception(() => store.Load()));
    }

    [Fact]
    public void bootstrap_validation_warnings_are_available_once_to_the_startup_diagnostic_owner()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """{ "formatVersion": 1, "language": "klingon", "theme": "dark" }""");
        var warnings = new List<string>();
        var store = new ConfigStore(path, warnings.Add);
        var before = File.ReadAllBytes(path);

        Assert.Equal(Languages.System, LanguageBootstrap.ReadPreference(store));
        Assert.Equal(["language"], warnings);
        Assert.Equal("dark", store.Load().Theme.ToString().ToLowerInvariant());
        Assert.Equal(["language"], warnings);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [MacOnlyFact]
    public void on_macOS_appkit_is_pointed_at_the_language_for_this_process_only()
    {
        var defaults = Send(Class("NSUserDefaults"), "standardUserDefaults");
        var argumentDomain = NSString("NSArgumentDomain");
        var previous = Send(defaults, "volatileDomainForName:", argumentDomain);
        try
        {
            LanguageBootstrap.AlignAppKit("ko");

            var languages = Send(defaults, "objectForKey:", NSString("AppleLanguages"));
            Assert.Equal(1UL, SendForUInt(languages, "count"));
            Assert.Equal("ko", String(SendWithIndex(languages, "objectAtIndex:", 0)));
        }
        finally
        {
            // The argument domain is process-wide; give the rest of the run the one it started with.
            if (previous == System.IntPtr.Zero)
                Send(defaults, "removeVolatileDomainForName:", argumentDomain);
            else
                Send(defaults, "setVolatileDomain:forName:", previous, argumentDomain);
        }
    }

    private static string SavedPreference(string? configFile) =>
        LanguageBootstrap.ReadPreference(configFile is null ? null : new ConfigStore(configFile, _ => { }));

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
