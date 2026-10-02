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
    public void Copy_owns_its_binder_entries()
    {
        var original = new AppConfig { Binders = new() { new KnownBinder { Path = "binder.daynote", Title = "Original" } } };
        var draft = original.Copy();
        draft.Binders[0].Title = "Draft";
        Assert.Equal("Original", original.Binders[0].Title);
    }

    [Fact]
    public void Settings_travel_without_the_binder_list()
    {
        var live = new AppConfig { Binders = new() { new KnownBinder { Path = "binder.daynote", Title = "Kept" } } };
        var draft = live.CopySettings();
        draft.Theme = ThemePreference.Dark;
        draft.TextStyles[0].FontSize = 20;

        live.TakeSettings(draft);
        draft.TextStyles[0].FontSize = 30;

        Assert.Empty(draft.Binders);
        Assert.Equal("Kept", Assert.Single(live.Binders).Title);
        Assert.Equal(ThemePreference.Dark, live.Theme);
        Assert.Equal(20, live.TextStyles[0].FontSize);
    }

    [Fact]
    public void Default_ui_font_is_stored_empty_and_resolves_to_the_bundled_inter()
    {
        Assert.Equal("", new AppConfig().UiFontFamily);
        Assert.Equal("Inter", AppConfig.DefaultUiFontFamily);
    }

    [Fact]
    public void A_stored_value_equal_to_the_former_default_is_kept()
    {
        const string json = """{ "uiFontFamily": "Inter" }""";
        var restored = JsonSerializer.Deserialize<AppConfig>(json, DayNoteJson.Options)!;
        Assert.Equal("Inter", restored.UiFontFamily);
    }

    [Fact]
    public void A_stored_value_different_from_the_default_is_kept()
    {
        const string json = """{ "uiFontFamily": "Helvetica Neue" }""";
        var restored = JsonSerializer.Deserialize<AppConfig>(json, DayNoteJson.Options)!;
        Assert.Equal("Helvetica Neue", restored.UiFontFamily);
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
    public void A_load_preserves_default_flags_and_resolution_falls_back_to_the_first()
    {
        const string none = """{ "textStyles": [ { "fontFamily": "A" }, { "fontFamily": "B" } ] }""";
        const string two = """{ "textStyles": [ { "fontFamily": "A" }, { "fontFamily": "B", "isDefault": true }, { "fontFamily": "C", "isDefault": true } ] }""";

        Assert.Equal(new[] { false, false }, JsonSerializer.Deserialize<AppConfig>(none, DayNoteJson.Options)!.TextStyles.Select(style => style.IsDefault));
        Assert.Equal(new[] { false, true, true }, JsonSerializer.Deserialize<AppConfig>(two, DayNoteJson.Options)!.TextStyles.Select(style => style.IsDefault));
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
            new[] { "Menlo 14", "Menlo 18.5", "Inter", "No font family", "\u904a\u660e\u671d\u4f53" },
            TextStyleLabels.For(styles, "No font family", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_size_in_a_label_is_written_in_the_readers_culture()
    {
        var styles = new[]
        {
            new EditorTextStyle { FontFamily = "Menlo", FontSize = 14 },
            new EditorTextStyle { FontFamily = "Menlo", FontSize = 18.5 },
        };

        Assert.Equal(
            new[] { "Menlo 14", "Menlo 18,5" },
            TextStyleLabels.For(styles, "-", System.Globalization.CultureInfo.GetCultureInfo("de")));
    }

    [Fact]
    public void The_language_defaults_to_system_and_is_copied()
    {
        Assert.Equal("system", new AppConfig().Language);
        Assert.Equal("ja", new AppConfig { Language = "ja" }.Copy().Language);
    }

    [Fact]
    public void The_time_zone_defaults_to_system()
    {
        Assert.Equal(DayNote.Core.Time.DayNoteTime.SystemZone, new AppConfig().TimeZone);
        Assert.Equal("system", DayNote.Core.Time.DayNoteTime.SystemZone);
    }

    [Fact]
    public void Copy_preserves_the_time_zone()
    {
        Assert.Equal("Europe/Paris", new AppConfig { TimeZone = "Europe/Paris" }.Copy().TimeZone);
    }
}
