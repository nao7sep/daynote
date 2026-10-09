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
public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daynote-config-" + IdGenerator.New());
    private readonly string? _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
    private readonly List<string> _warnings = new();
    private string ConfigPath => Path.Combine(_directory, "config.json");
    private ConfigStore Store => new(ConfigPath, _warnings.Add);

    public ConfigStoreTests()
    {
        Directory.CreateDirectory(_directory);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _directory);
    }

    [Fact]
    public void A_missing_config_uses_built_ins_without_writing()
    {
        Assert.Equal("system", Store.Load().Language);
        Assert.False(File.Exists(ConfigPath));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("""{ "theme": "dark" }""")] // no format version
    [InlineData("""{ "formatVersion": 1, "theme": "dark", "theme": "light" }""")] // a repeated key
    public void A_malformed_config_throws_and_is_left_byte_identical(string json)
    {
        File.WriteAllText(ConfigPath, json);
        var before = File.ReadAllBytes(ConfigPath);

        Assert.Throws<InvalidDataException>(() => Store.Load());

        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void One_set_uses_built_ins_for_every_absent_set_and_keeps_unknown_keys_on_write()
    {
        File.WriteAllText(ConfigPath, """{ "formatVersion": 1, "theme": "dark", "version": 2, "future": true }""");
        var store = Store;
        var config = store.Load();
        var builtIns = new AppConfig();
        Assert.Equal(ThemePreference.Dark, config.Theme);
        Assert.Equal(builtIns.Language, config.Language);
        Assert.Equal(builtIns.UiFontFamily, config.UiFontFamily);
        Assert.Equal(builtIns.AutosaveDelaySeconds, config.AutosaveDelaySeconds);
        Assert.Equal(builtIns.TimeZone, config.TimeZone);
        Assert.Equal(JsonSerializer.Serialize(builtIns.TextStyles), JsonSerializer.Serialize(config.TextStyles));
        config.UiFontFamily = "Inter";
        store.Save(config);
        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal(new[] { "formatVersion", "uiFontFamily", "theme", "version", "future" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(2, saved.RootElement.GetProperty("version").GetInt32());
        Assert.True(saved.RootElement.GetProperty("future").GetBoolean());
        Assert.Equal("Inter", Store.Load().UiFontFamily);
    }

    [Theory]
    [InlineData("textStyles", "[{\"fontFamily\":\"A\"}]")]
    [InlineData("binders", "[{\"path\":\"x\"}]")]
    public void An_invalid_authored_set_and_an_unknown_key_survive_other_saves_unchanged(string key, string value)
    {
        File.WriteAllText(ConfigPath, $"{{\"formatVersion\":1,\"{key}\":{value},\"future\":{{\"a\":[1,2]}}}}");
        var store = Store;
        var config = store.Load();

        config.Theme = ThemePreference.Dark;
        store.Save(config);
        config.Binders.Add(new KnownBinder { Path = "/tmp/other.daynote", Title = "Other" });
        config.Binders.Clear();
        config.UiFontFamily = "Menlo";
        store.Save(config);

        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(value).RootElement, saved.RootElement.GetProperty(key)));
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse("""{"a":[1,2]}""").RootElement, saved.RootElement.GetProperty("future")));
        Assert.Equal("dark", saved.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public void Changing_an_invalid_authored_set_replaces_its_stored_value()
    {
        File.WriteAllText(ConfigPath, """{"formatVersion":1,"textStyles":[{"fontFamily":"A"}]}""");
        var store = Store;
        var config = store.Load();

        config.TextStyles[0].FontSize = 22;
        store.Save(config);
        config.Theme = ThemePreference.Dark;
        store.Save(config);

        Assert.Equal(22, Store.Load().TextStyles[0].FontSize);
        Assert.Single(_warnings);
    }

    [Fact]
    public void An_invalid_preference_is_normalized_by_the_next_save()
    {
        File.WriteAllText(ConfigPath, """{"formatVersion":1,"theme":"sepia","language":"xx","autosaveDelaySeconds":3}""");
        var store = Store;
        var config = store.Load();

        config.AutosaveDelaySeconds = 4;
        store.Save(config);

        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal(new[] { "formatVersion", "autosaveDelaySeconds" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData("JA", "ja")]
    [InlineData("Ja", "ja")]
    [InlineData(" pt-br ", "pt-BR")]
    [InlineData("System", "system")]
    public void A_language_matching_an_offered_tag_ignoring_case_reads_and_saves_as_that_tag(string stored, string canonical)
    {
        File.WriteAllText(ConfigPath, $$"""{"formatVersion":1,"language":"{{stored}}","theme":"dark"}""");
        var store = Store;

        var config = store.Load();
        store.Save(config);

        Assert.Equal(canonical, config.Language);
        Assert.Empty(_warnings);
        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        if (canonical == AppConfig.SystemLanguage)
            Assert.False(saved.RootElement.TryGetProperty("language", out _));
        else
            Assert.Equal(canonical, saved.RootElement.GetProperty("language").GetString());
    }

    [Theory]
    [InlineData("theme", "\"unknown\"")]
    [InlineData("theme", "4")]
    [InlineData("language", "null")]
    [InlineData("language", "\"xx\"")]
    [InlineData("uiFontFamily", "[]")]
    [InlineData("autosaveDelaySeconds", "\"2\"")]
    [InlineData("timeZone", "false")]
    [InlineData("timeZone", "\"Mars/Phobos\"")]
    [InlineData("autosaveDelaySeconds", "0")]
    [InlineData("textStyles", "null")]
    [InlineData("textStyles", "[{\"fontFamily\":\"A\"}]")]
    [InlineData("textStyles", "[null]")]
    [InlineData("textStyles", "[{\"isDefault\":false,\"fontFamily\":\"A\",\"fontSize\":14,\"lineSpacing\":1.4,\"padding\":12,\"bold\":false,\"italic\":false}]")]
    [InlineData("textStyles", "[{\"isDefault\":true,\"fontFamily\":\"A\",\"fontSize\":70,\"lineSpacing\":1.4,\"padding\":12,\"bold\":false,\"italic\":false}]")]
    [InlineData("binders", "[{\"path\":\"x\"}]")]
    [InlineData("binders", "[null]")]
    public void A_set_that_fails_its_check_falls_back_only_for_that_set_and_warns(string key, string value)
    {
        File.WriteAllText(ConfigPath, $"{{\"formatVersion\":1,\"{key}\":{value}}}");
        var config = Store.Load();
        Assert.Empty(ConfigSets.UserSets(config));
        Assert.Equal(key, Assert.Single(_warnings));
        Assert.True(File.Exists(ConfigPath));
    }

    [Theory]
    [InlineData("DARK", true)]
    [InlineData("light", true)]
    [InlineData("2", false)]
    [InlineData("sepia", false)]
    public void The_reader_and_the_converter_accept_the_same_themes(string name, bool accepted)
    {
        File.WriteAllText(ConfigPath, $"{{\"formatVersion\":1,\"theme\":\"{name}\"}}");
        Assert.Equal(accepted, ThemePreferenceJsonConverter.TryParse(name, out var parsed));
        Assert.Equal(accepted ? parsed : ThemePreference.System, Store.Load().Theme);
        Assert.Equal(accepted ? 0 : 1, _warnings.Count);
    }

    [Fact]
    public void Whole_text_styles_are_preserved_without_default_merge()
    {
        var config = new AppConfig();
        config.TextStyles = new() { new EditorTextStyle { IsDefault = true, FontFamily = "Custom", FontSize = 30 } };
        Store.Save(config);
        var restored = Store.Load();
        var style = Assert.Single(restored.TextStyles);
        Assert.True(style.IsDefault);
        Assert.Equal(30, style.FontSize);
        Assert.Equal("Custom", style.FontFamily);
    }

    [Fact]
    public void A_set_saved_equal_to_its_built_in_loses_its_key()
    {
        var config = new AppConfig { Theme = ThemePreference.Dark };
        config.TextStyles[0].FontSize = 22;
        Store.Save(config);
        config.TextStyles = AppConfig.DefaultTextStyles();
        Store.Save(config);
        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal(new[] { "formatVersion", "theme" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void Save_writes_nothing_when_the_map_would_not_change()
    {
        var store = Store;
        store.Save(store.Load());
        Assert.False(File.Exists(ConfigPath));

        File.WriteAllText(ConfigPath, """{ "formatVersion": 1 }""");
        store = Store;
        store.Save(store.Load());
        Assert.Equal("""{ "formatVersion": 1 }""", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Save_writes_from_memory_without_reading_the_file()
    {
        var store = Store;
        var config = store.Load();
        File.WriteAllText(ConfigPath, "{ broken");
        config.Theme = ThemePreference.Dark;
        store.Save(config);
        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal(new[] { "formatVersion", "theme" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void Save_heals_every_set_from_memory()
    {
        File.WriteAllText(ConfigPath, """{ "formatVersion": 1, "timeZone": "Mars/Phobos", "theme": "system", "uiFontFamily": "Menlo" }""");
        var store = Store;
        store.Save(store.Load());
        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal(new[] { "formatVersion", "uiFontFamily" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
    }

    public void Dispose()
    {
        BackupStore.Close();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        Directory.Delete(_directory, recursive: true);
    }
}
