using System.IO;
using DayNote.Core.Identity;
using DayNote.I18n;
using Xunit;

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

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
