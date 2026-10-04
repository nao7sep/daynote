using System.IO;
using DayNote.Core.Identity;
using DayNote.I18n;
using Xunit;
using static DayNote.Views.ObjC;

namespace DayNote.Tests.I18n;

/// <summary>
/// The language is read straight out of <c>config.json</c> before the app is built, forgivingly: a
/// file that cannot say which language means System, never a failure to start.
/// </summary>
public sealed class LanguageBootstrapTests : System.IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daynote-bootstrap-" + IdGenerator.New());

    public LanguageBootstrapTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("""{ "language": "ja" }""", "ja")]
    [InlineData("""{ "language": "pt-br", "theme": "dark" }""", "pt-BR")]
    [InlineData("""{ "language": "JA" }""", "ja")]
    [InlineData("""{ "language": "Ja" }""", "ja")]
    [InlineData("""{ "language": "klingon" }""", Languages.System)]
    [InlineData("""{ "language": 3 }""", Languages.System)]
    [InlineData("""{ "theme": "dark" }""", Languages.System)]
    [InlineData("""[ "ja" ]""", Languages.System)]
    [InlineData("""{ "language": """, Languages.System)]
    public void the_saved_preference_is_read_forgivingly(string json, string expected)
    {
        var config = Path.Combine(_directory, "config.json");
        File.WriteAllText(config, json);

        Assert.Equal(expected, LanguageBootstrap.SavedPreference(config));
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
        File.WriteAllText(config, $$"""{ "language": "{{stored}}" }""");

        var settings = new DayNote.Core.Storage.ConfigStore(config, _ => { }).Load();

        Assert.Equal(settings.Language, LanguageBootstrap.SavedPreference(config));
    }

    [Fact]
    public void no_file_or_no_storage_means_system()
    {
        Assert.Equal(Languages.System, LanguageBootstrap.SavedPreference(Path.Combine(_directory, "missing.json")));
        Assert.Equal(Languages.System, LanguageBootstrap.SavedPreference(null));
    }

    [Fact]
    public void the_setting_the_app_writes_is_the_one_the_bootstrap_reads()
    {
        var config = Path.Combine(_directory, "config.json");
        File.WriteAllText(config, System.Text.Json.JsonSerializer.Serialize(
            new DayNote.Core.Configuration.AppConfig { Language = "ko" }, DayNote.Core.Configuration.DayNoteJson.Options));

        Assert.Equal("ko", LanguageBootstrap.SavedPreference(config));
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

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
