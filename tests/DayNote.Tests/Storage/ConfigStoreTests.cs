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
        Store.Save(config, new[] { "uiFontFamily" });
        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal(new[] { "theme", "uiFontFamily" }, saved.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Inter", Store.Load().UiFontFamily);
    }

    [Theory]
    [InlineData("theme", "\"unknown\"")]
    [InlineData("theme", "4")]
    [InlineData("language", "null")]
    [InlineData("uiFontFamily", "[]")]
    [InlineData("autosaveDelaySeconds", "\"2\"")]
    [InlineData("timeZone", "false")]
    [InlineData("textStyles", "null")]
    [InlineData("textStyles", "[{\"fontFamily\":\"A\"}]")]
    [InlineData("textStyles", "[null]")]
    [InlineData("binders", "[{\"path\":\"x\"}]")]
    [InlineData("binders", "[null]")]
    public void A_wrong_shape_falls_back_only_for_that_set_and_warns_once(string key, string value)
    {
        File.WriteAllText(ConfigPath, $"{{\"{key}\":{value}}}");
        var config = Store.Load();
        Assert.False(SettingsValidator.IsDirty(config, new AppConfig()));
        Assert.Equal(key, Assert.Single(_warnings));
        Assert.True(File.Exists(ConfigPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.invalid"));
    }

    [Fact]
    public void Whole_text_styles_are_preserved_without_normalization_or_default_merge()
    {
        var config = new AppConfig();
        config.TextStyles = new() { new EditorTextStyle { IsDefault = false, FontFamily = "Custom", FontSize = 70 } };
        Store.Save(config, new[] { "textStyles" });
        var restored = Store.Load();
        var style = Assert.Single(restored.TextStyles);
        Assert.False(style.IsDefault);
        Assert.Equal(70, style.FontSize);
        Assert.Equal("Custom", style.FontFamily);
    }

    [Fact]
    public void Reset_deletes_the_set_while_preserving_other_copies()
    {
        var config = new AppConfig { Theme = ThemePreference.Dark };
        Store.Save(config, new[] { "theme", "textStyles" });
        Store.Save(config, Array.Empty<string>(), resetTextStyles: true);
        using var saved = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal("theme", Assert.Single(saved.RootElement.EnumerateObject()).Name);
        Assert.Equal(ThemePreference.Dark, Store.Load().Theme);
        Assert.False(SettingsValidator.IsDirty(new AppConfig(), new AppConfig { TextStyles = Store.Load().TextStyles }));
    }

    [Fact]
    public void Editing_a_reset_draft_saves_the_users_copy()
    {
        var config = new AppConfig();
        config.TextStyles[0].FontSize = 22;
        Store.Save(config, Array.Empty<string>(), resetTextStyles: true);
        Assert.Equal(22, Store.Load().TextStyles[0].FontSize);
    }

    [Fact]
    public void Read_modify_write_preserves_a_set_written_after_the_dialog_opened()
    {
        var original = Store.Load();
        var draft = original.Copy();
        Store.Save(new AppConfig { TimeZone = "Europe/London" }, new[] { "timeZone" });
        draft.Theme = ThemePreference.Light;
        Store.Save(draft, ConfigSets.ChangedKeys(draft, original));
        Assert.Equal("Europe/London", Store.Load().TimeZone);
        Assert.Equal(ThemePreference.Light, Store.Load().Theme);
    }

    public void Dispose()
    {
        BackupStore.Close();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        Directory.Delete(_directory, recursive: true);
    }
}
