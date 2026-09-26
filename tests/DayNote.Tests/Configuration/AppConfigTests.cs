using System.Linq;
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

    [Fact]
    public void A_config_that_selected_a_named_preset_migrates_to_the_default_flag()
    {
        // The shape written before presets went by their font family.
        const string legacy = """
            {
              "textStyles": [
                { "name": "Mono", "fontFamily": "メイリオ", "fontSize": 14 },
                { "name": "Sans", "fontFamily": "Inter", "fontSize": 15 },
                { "name": "Prank", "fontFamily": "遊明朝体", "fontSize": 15 }
              ],
              "selectedTextStyle": "prank"
            }
            """;

        var config = JsonSerializer.Deserialize<AppConfig>(legacy, DayNoteJson.Options)!;

        Assert.Equal(new[] { false, false, true }, config.TextStyles.Select(style => style.IsDefault));
        Assert.Same(config.TextStyles[2], config.ResolveDefaultStyle());
        var saved = JsonSerializer.Serialize(config, DayNoteJson.Options);
        Assert.DoesNotContain("\"name\"", saved);
        Assert.DoesNotContain("selectedTextStyle", saved);
        Assert.Contains("\"isDefault\": true", saved);
    }

    [Fact]
    public void A_load_keeps_exactly_one_default_and_falls_back_to_the_first()
    {
        const string none = """{ "textStyles": [ { "fontFamily": "A" }, { "fontFamily": "B" } ] }""";
        const string two = """{ "textStyles": [ { "fontFamily": "A" }, { "fontFamily": "B", "isDefault": true }, { "fontFamily": "C", "isDefault": true } ] }""";

        Assert.Equal(new[] { true, false }, JsonSerializer.Deserialize<AppConfig>(none, DayNoteJson.Options)!.TextStyles.Select(style => style.IsDefault));
        Assert.Equal(new[] { false, true, false }, JsonSerializer.Deserialize<AppConfig>(two, DayNoteJson.Options)!.TextStyles.Select(style => style.IsDefault));
    }

    [Fact]
    public void The_built_in_presets_have_exactly_one_default()
    {
        Assert.Single(new AppConfig().TextStyles, style => style.IsDefault);
    }

    [Fact]
    public void Labels_are_the_family_with_the_size_only_where_two_would_match()
    {
        var styles = new[]
        {
            new EditorTextStyle { FontFamily = "Menlo", FontSize = 14 },
            new EditorTextStyle { FontFamily = "Menlo, Consolas, monospace", FontSize = 18.5 },
            new EditorTextStyle { FontFamily = "Inter", FontSize = 15 },
            new EditorTextStyle { FontFamily = " ", FontSize = 15 },
            // A family no system has still goes by the name it was given, not by what it falls back to.
            new EditorTextStyle { FontFamily = "\u904a\u660e\u671d\u4f53", FontSize = 15 },
        };

        Assert.Equal(
            new[] { "Menlo 14", "Menlo 18.5", "Inter", TextStyleLabels.NoFontFamily, "\u904a\u660e\u671d\u4f53" },
            TextStyleLabels.For(styles));
    }

    [Fact]
    public void The_time_zone_defaults_to_system()
    {
        Assert.Equal(DayNote.Core.Time.DayNoteTime.SystemZone, new AppConfig().TimeZone);
        Assert.Equal("system", DayNote.Core.Time.DayNoteTime.SystemZone);
    }

    [Theory]
    // The zone every older config was seeded with carries no choice, so it follows the computer.
    [InlineData("Asia/Tokyo", "system")]
    // A zone the user typed stays chosen.
    [InlineData("Europe/London", "Europe/London")]
    [InlineData(" America/New_York ", "America/New_York")]
    // A blank or an id no zone answers to already displayed as UTC by accident; it follows the computer.
    [InlineData("", "system")]
    [InlineData("Mars/Phobos", "system")]
    public void An_older_typed_zone_migrates_to_the_list(string legacy, string expected)
    {
        var json = $$"""{ "displayTimeZone": "{{legacy}}" }""";

        var config = JsonSerializer.Deserialize<AppConfig>(json, DayNoteJson.Options)!;

        Assert.Equal(expected, config.TimeZone);
        var saved = JsonSerializer.Serialize(config, DayNoteJson.Options);
        Assert.DoesNotContain("displayTimeZone", saved);
        Assert.Contains($"\"timeZone\": \"{expected}\"", saved);
    }

    [Fact]
    public void A_zone_chosen_from_the_list_is_kept_over_a_leftover_typed_one()
    {
        const string json = """{ "timeZone": "Europe/Paris", "displayTimeZone": "Asia/Tokyo" }""";

        Assert.Equal("Europe/Paris", JsonSerializer.Deserialize<AppConfig>(json, DayNoteJson.Options)!.TimeZone);
    }

    [Fact]
    public void Copy_preserves_the_time_zone()
    {
        Assert.Equal("Europe/Paris", new AppConfig { TimeZone = "Europe/Paris" }.Copy().TimeZone);
    }
}
