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
    public void Missing_and_quarantined_config_use_built_ins_without_writing()
    {
        Assert.Equal("system", Store.Load().Language);
        Assert.False(File.Exists(ConfigPath));
        File.WriteAllText(ConfigPath, "{ broken");
        Assert.Equal("system", Store.Load().Language);
        Assert.False(File.Exists(ConfigPath));
        Assert.Single(Directory.GetFiles(_directory, "*.invalid"));
    }

    [Fact]
    public void One_set_uses_built_ins_for_every_absent_set_and_drops_unknown_keys_on_write()
    {
        File.WriteAllText(ConfigPath, """{ "theme": "dark", "version": 2, "future": true }""");
        var config = Store.Load();
        var builtIns = new AppConfig();
        Assert.Equal(ThemePreference.Dark, config.Theme);
        Assert.Equal(builtIns.Language, config.Language);
        Assert.Equal(builtIns.UiFontFamily, config.UiFontFamily);
        Assert.Equal(builtIns.AutosaveDelaySeconds, config.AutosaveDelaySeconds);
        Assert.Equal(builtIns.TimeZone, config.TimeZone);
        Assert.Equal(JsonSerializer.Serialize(builtIns.TextStyles), JsonSerializer.Serialize(config.TextStyles));
        config.UiFontFamily = "Inter";
        Store.Save(config);
        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal(new[] { "uiFontFamily", "theme" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Inter", Store.Load().UiFontFamily);
    }

    [Theory]
    [InlineData("theme", "\"unknown\"")]
    [InlineData("theme", "4")]
    [InlineData("language", "null")]
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
        File.WriteAllText(ConfigPath, $"{{\"{key}\":{value}}}");
        var config = Store.Load();
        Assert.Empty(ConfigSets.UserSets(config));
        Assert.Equal(key, Assert.Single(_warnings));
        Assert.True(File.Exists(ConfigPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.invalid"));
    }

    [Theory]
    [InlineData("DARK", true)]
    [InlineData("light", true)]
    [InlineData("2", false)]
    [InlineData("sepia", false)]
    public void The_reader_and_the_converter_accept_the_same_themes(string name, bool accepted)
    {
        File.WriteAllText(ConfigPath, $"{{\"theme\":\"{name}\"}}");
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
        Assert.Equal("theme", Assert.Single(saved.RootElement.EnumerateObject()).Name);
    }

    [Fact]
    public void Save_writes_nothing_when_the_map_would_not_change()
    {
        Store.Save(new AppConfig());
        Assert.False(File.Exists(ConfigPath));

        File.WriteAllText(ConfigPath, "{ }");
        Store.Save(new AppConfig());
        Assert.Equal("{ }", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Save_heals_every_set_from_memory()
    {
        File.WriteAllText(ConfigPath, """{ "timeZone": "Mars/Phobos", "theme": "system", "uiFontFamily": "Menlo" }""");
        Store.Save(Store.Load());
        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal("uiFontFamily", Assert.Single(saved.RootElement.EnumerateObject()).Name);
    }

    public void Dispose()
    {
        BackupStore.Close();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        Directory.Delete(_directory, recursive: true);
    }
}
