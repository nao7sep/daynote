using System;
using System.IO;
using System.Text.Json;
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
public sealed class JsonStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;
    private readonly JsonStore<AppConfig> _store;

    public JsonStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daynote-json-tests-" + IdGenerator.New());
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "config.json");
        _store = new JsonStore<AppConfig>(_path);
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
                new EditorTextStyle { Name = "Custom", FontFamily = "Cascadia Code", FontSize = 17, LineSpacing = 1.6, Padding = 10, Bold = true },
            },
            SelectedTextStyle = "Custom",
            DisplayTimeZone = "Europe/London",
        });

        var loaded = _store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("Custom", loaded!.SelectedTextStyle);
        Assert.Equal("Europe/London", loaded.DisplayTimeZone);
        var style = Assert.Single(loaded.TextStyles);
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
        _store.Save(new AppConfig { SelectedTextStyle = "First" });
        _store.Save(new AppConfig { SelectedTextStyle = "Second" });

        Assert.Equal("Second", _store.Load()!.SelectedTextStyle);
    }

    [Fact]
    public void Load_throws_on_corrupt_json_so_the_caller_can_gate_saving()
    {
        File.WriteAllText(_path, "{ this is not valid json");

        Assert.Throws<JsonException>(() => _store.Load());
    }

    [Fact]
    public void CreateIfMissing_writes_the_defaults_on_first_run()
    {
        var created = _store.CreateIfMissing(new AppConfig());

        Assert.True(created);
        Assert.True(File.Exists(_path));
        // Produced through Save, so it is the canonical form and round-trips.
        Assert.EndsWith("\n", File.ReadAllText(_path));
        Assert.NotNull(_store.Load());
    }

    [Fact]
    public void CreateIfMissing_never_touches_an_existing_file()
    {
        _store.Save(new AppConfig { SelectedTextStyle = "Mine" });
        var before = File.ReadAllText(_path);

        // A different value must not overwrite: the single trigger is absence, so a good (possibly
        // hand-edited) file is left byte-for-byte as it was.
        var created = _store.CreateIfMissing(new AppConfig { SelectedTextStyle = "Default" });

        Assert.False(created);
        Assert.Equal(before, File.ReadAllText(_path));
        Assert.Equal("Mine", _store.Load()!.SelectedTextStyle);
    }

    public void Dispose()
    {
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
