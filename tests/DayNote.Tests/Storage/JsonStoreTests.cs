using System;
using System.IO;
using System.Text.Json;
using DayNote.Core.Configuration;
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
        _directory = Path.Combine(Path.GetTempPath(), "daynote-json-tests-" + Guid.NewGuid().ToString("N"));
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
        _store.Save(new AppConfig { EditorFont = "Cascadia Code", EditorFontSize = 17, DisplayTimeZone = "Europe/London" });

        var loaded = _store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("Cascadia Code", loaded!.EditorFont);
        Assert.Equal(17, loaded.EditorFontSize);
        Assert.Equal("Europe/London", loaded.DisplayTimeZone);
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
        _store.Save(new AppConfig { EditorFont = "First" });
        _store.Save(new AppConfig { EditorFont = "Second" });

        Assert.Equal("Second", _store.Load()!.EditorFont);
    }

    [Fact]
    public void Load_throws_on_corrupt_json_so_the_caller_can_gate_saving()
    {
        File.WriteAllText(_path, "{ this is not valid json");

        Assert.Throws<JsonException>(() => _store.Load());
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
