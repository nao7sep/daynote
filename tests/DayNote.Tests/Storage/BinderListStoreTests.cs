using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DayNote.Core.Backup;
using DayNote.Core.Configuration;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

[Collection(AppPathsEnvironment.CollectionName)]
public sealed class BinderListStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daynote-binders-" + IdGenerator.New());
    private readonly string? _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
    private readonly List<string> _warnings = new();
    private string ListPath => Path.Combine(_directory, "binders.json");
    private BinderListStore Store => new(ListPath, _warnings.Add);

    public BinderListStoreTests()
    {
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _directory);
    }

    private void WriteList(JsonElement binders, string extra = "") =>
        File.WriteAllText(ListPath, $"{{\"formatVersion\":1,\"binders\":{binders.GetRawText()}{extra}}}");

    private JsonElement SavedList()
    {
        using var saved = JsonDocument.Parse(File.ReadAllText(ListPath));
        return saved.RootElement.GetProperty("binders").Clone();
    }

    [Fact]
    public void No_file_is_an_empty_list_and_an_empty_list_writes_nothing()
    {
        var store = Store;
        Assert.Empty(store.Load());
        store.Save([]);
        Assert.False(File.Exists(ListPath));
    }

    [Fact]
    public void A_list_saves_whole_in_order_and_reads_back()
    {
        var store = Store;
        store.Load();
        store.Save([new KnownBinder { Path = "/b.daynote", Title = "B" }, new KnownBinder { Path = "/a.daynote", Title = "A" }]);

        using var saved = JsonDocument.Parse(File.ReadAllText(ListPath));
        Assert.Equal(new[] { "formatVersion", "binders" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(new[] { ("/b.daynote", "B"), ("/a.daynote", "A") }, Store.Load().Select(entry => (entry.Path, entry.Title)));
    }

    [Fact]
    public void A_moved_list_is_written_once_as_stored_and_only_while_there_is_no_file()
    {
        var moved = JsonDocument.Parse("""[{"path":"/a.daynote","title":"A"}]""").RootElement;

        Assert.Equal("A", Assert.Single(Store.Load(moved)).Title);
        Assert.True(JsonElement.DeepEquals(moved, SavedList()));

        var other = JsonDocument.Parse("""[{"path":"/b.daynote","title":"B"}]""").RootElement;
        Assert.Equal("A", Assert.Single(Store.Load(other)).Title);
        Assert.True(JsonElement.DeepEquals(moved, SavedList()));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("""{ "binders": [] }""")] // no format version
    [InlineData("""{ "formatVersion": 2, "binders": [] }""")] // newer
    public void A_file_that_cannot_be_used_throws_and_is_left_byte_identical(string json)
    {
        File.WriteAllText(ListPath, json);
        var before = File.ReadAllBytes(ListPath);

        Assert.ThrowsAny<Exception>(() => Store.Load(JsonDocument.Parse("[]").RootElement));

        Assert.Equal(before, File.ReadAllBytes(ListPath));
    }

    [Theory]
    [InlineData("""[{"path":"x"}]""")]
    [InlineData("[null]")]
    [InlineData("\"binders\"")]
    [InlineData("""[{"path":"","title":"Blank"}]""")]
    [InlineData("""[{"path":"bad\u0000path","title":"Nul"}]""")]
    public void An_invalid_list_reads_as_none_with_a_warning_and_is_kept_until_the_list_changes(string value)
    {
        var stored = JsonDocument.Parse(value).RootElement;
        WriteList(stored, ",\"future\":{\"a\":[1,2]}");
        var store = Store;

        Assert.Empty(store.Load());
        Assert.Equal("binders", Assert.Single(_warnings));
        store.Save([]);
        Assert.True(JsonElement.DeepEquals(stored, SavedList()));

        store.Save([new KnownBinder { Path = "/a.daynote", Title = "A" }]);
        store.Save([]);
        Assert.Equal(0, SavedList().GetArrayLength());
        using var saved = JsonDocument.Parse(File.ReadAllText(ListPath));
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse("""{"a":[1,2]}""").RootElement, saved.RootElement.GetProperty("future")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_normalized_binder_paths_are_rejected_in_either_order(bool reverse)
    {
        var entries = new List<KnownBinder>
        {
            new() { Path = Path.Combine(_directory, "same.daynote"), Title = "First" },
            new() { Path = Path.Combine(_directory, ".", "same.daynote"), Title = "Alias" },
            new() { Path = Path.Combine(_directory, "other.daynote"), Title = "Other" },
        };
        if (reverse)
            entries.Reverse();
        WriteList(JsonSerializer.SerializeToElement(entries, DayNoteJson.Options));

        Assert.Empty(Store.Load());
        Assert.Equal("binders", Assert.Single(_warnings));
    }

    [Fact]
    public void Offline_binders_keep_their_literal_paths_and_distinct_case()
    {
        var entries = new List<KnownBinder>
        {
            new() { Path = Path.Combine(_directory, "offline", "..", "Binder.daynote"), Title = "Upper" },
            new() { Path = Path.Combine(_directory, "binder.daynote"), Title = "Lower" },
        };
        WriteList(JsonSerializer.SerializeToElement(entries, DayNoteJson.Options));

        Assert.Equal(entries.Select(entry => entry.Path), Store.Load().Select(entry => entry.Path));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void A_failed_replacement_of_an_invalid_list_keeps_it()
    {
        var stored = JsonDocument.Parse("""[{"path":"x"}]""").RootElement;
        WriteList(stored);
        var original = File.ReadAllText(ListPath);
        var store = Store;
        store.Load();

        // A directory at the destination rejects publication on either platform.
        File.Delete(ListPath);
        Directory.CreateDirectory(ListPath);
        try
        {
            var error = Record.Exception(() => store.Save([new KnownBinder { Path = "/a.daynote", Title = "A" }]));
            Assert.True(error is IOException or UnauthorizedAccessException, $"Expected a publication failure, got {error}");
        }
        finally
        {
            Directory.Delete(ListPath);
            File.WriteAllText(ListPath, original);
        }

        store.Save([]);
        Assert.True(JsonElement.DeepEquals(stored, SavedList()));
    }

    public void Dispose()
    {
        BackupStore.Close();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        Directory.Delete(_directory, recursive: true);
    }
}
