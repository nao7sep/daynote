using System.Text.Json;
using DayNote.Core.Configuration;
using Xunit;

namespace DayNote.Tests.Configuration;

/// <summary>
/// Guards the UI-font preference added to <see cref="AppConfig"/>: its default, deep-copy fidelity
/// (the settings dialog edits a copy), and JSON persistence.
/// </summary>
public sealed class AppConfigTests
{
    [Fact]
    public void Default_ui_font_is_the_bundled_inter()
    {
        Assert.Equal("Inter", new AppConfig().UiFontFamily);
        Assert.Equal("Inter", AppConfig.DefaultUiFontFamily);
    }

    [Fact]
    public void Copy_preserves_the_ui_font()
    {
        var config = new AppConfig { UiFontFamily = "Iosevka, monospace" };
        Assert.Equal("Iosevka, monospace", config.Copy().UiFontFamily);
    }

    [Fact]
    public void Json_round_trips_the_ui_font()
    {
        var config = new AppConfig { UiFontFamily = "Helvetica Neue" };
        var json = JsonSerializer.Serialize(config, DayNoteJson.Options);
        var restored = JsonSerializer.Deserialize<AppConfig>(json, DayNoteJson.Options)!;
        Assert.Equal("Helvetica Neue", restored.UiFontFamily);
    }
}
