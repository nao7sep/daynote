using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using DayNote.Core.Backup;
using DayNote.Core.Configuration;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// The JSON store backs the config and state files. Its contract is load-bearing for startup safety:
/// a missing file means first run (null, not an error), while a corrupt file must throw so the
/// caller can disable saving rather than overwrite good data. Writes are atomic and newline-terminated.
/// </summary>
/// <remarks>
/// A Save goes through the atomic writer, which is where saves reach the backup history, so
/// <c>DAYNOTE_DATA_DIR</c> is relocated to this test's throwaway directory to keep the store out of the
/// developer's real <c>~/.daynote/</c>. Joined to the AppPaths collection so that process-wide env var
/// never races; the store singleton is closed in teardown so it re-opens per throwaway root.
/// </remarks>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class JsonStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;
    private readonly string? _previousHome;
    private readonly JsonStore<AppConfig> _store;

    public JsonStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daynote-json-tests-" + IdGenerator.New());
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "config.json");
        _store = new JsonStore<AppConfig>(_path, FormatVersions.Config);

        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _directory);
    }

    [Fact]
    public void Load_returns_null_when_the_file_is_missing()
    {
        Assert.Null(_store.Load());
    }

    [Fact]
    public void Save_then_load_round_trips_the_value()
    {
        _store.Save(new AppConfig
        {
            TextStyles = new()
            {
                new EditorTextStyle { FontFamily = "Menlo", FontSize = 14 },
                new EditorTextStyle { IsDefault = true, FontFamily = "Cascadia Code", FontSize = 17, LineSpacing = 1.6, Padding = 10, Bold = true },
            },
            TimeZone = "Europe/London",
        });

        var loaded = _store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("Europe/London", loaded!.TimeZone);
        Assert.Equal(2, loaded.TextStyles.Count);
        var style = loaded.ResolveDefaultStyle()!;
        Assert.Same(loaded.TextStyles[1], style);
        Assert.Equal("Cascadia Code", style.FontFamily);
        Assert.Equal(17, style.FontSize);
        Assert.Equal(1.6, style.LineSpacing);
        Assert.True(style.Bold);
    }

    [Fact]
    public void Save_writes_a_trailing_newline()
    {
        _store.Save(new AppConfig());

        Assert.EndsWith("\n", File.ReadAllText(_path));
    }

    [Fact]
    public void Save_overwrites_an_existing_file()
    {
        _store.Save(new AppConfig { AutosaveDelaySeconds = 3 });
        _store.Save(new AppConfig { AutosaveDelaySeconds = 4 });

        Assert.Equal(4, _store.Load()!.AutosaveDelaySeconds);
    }

    [Fact]
    public void Load_refuses_corrupt_json_and_leaves_it_in_place()
    {
        // A load only reads: the caller decides what an unusable file means, and the bytes stay put.
        File.WriteAllText(_path, "{ this is not valid json");

        Assert.Throws<InvalidDataException>(() => _store.Load());

        Assert.Equal("{ this is not valid json", File.ReadAllText(_path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Save_writes_the_format_version_as_the_first_key()
    {
        _store.Save(new AppConfig());

        using var document = JsonDocument.Parse(File.ReadAllText(_path));
        var first = Assert.Single(document.RootElement.EnumerateObject().Take(1));
        Assert.Equal("formatVersion", first.Name);
        Assert.Equal(FormatVersions.Config, first.Value.GetInt32());
    }

    [Fact]
    public void A_file_recording_the_current_format_version_round_trips()
    {
        File.WriteAllText(_path, $$"""{"formatVersion":{{FormatVersions.Config}},"timeZone":"Europe/London"}""");

        Assert.Equal("Europe/London", _store.Load()!.TimeZone);
    }

    [Fact]
    public void A_newer_file_is_refused_and_left_in_place()
    {
        var newer = $$"""{"formatVersion":{{FormatVersions.Config + 1}},"timeZone":["Europe/London"]}""";
        File.WriteAllText(_path, newer);
        var before = File.ReadAllBytes(_path);

        var error = Assert.Throws<NewerFormatException>(() => _store.Load());

        Assert.Equal(FormatVersions.Config + 1, error.Found);
        Assert.Equal(before, File.ReadAllBytes(_path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("""{"timeZone":"Europe/London"}""")]
    [InlineData("""{"formatVersion":0}""")]
    [InlineData("""{"formatVersion":-1}""")]
    [InlineData("""{"formatVersion":1.5}""")]
    [InlineData("""{"formatVersion":"1"}""")]
    [InlineData("null")]
    public void A_file_without_a_positive_integer_format_version_is_refused_and_left_in_place(string json)
    {
        File.WriteAllText(_path, json);

        Assert.Throws<InvalidDataException>(() => _store.Load());

        Assert.Equal(json, File.ReadAllText(_path));
    }

    public void Dispose()
    {
        BackupStore.Close();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp directory is harmless.
        }
    }
}
