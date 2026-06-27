using System.Collections.Generic;

using DayNote.Core.Configuration;
using Xunit;

namespace DayNote.Tests.Configuration;

/// <summary>
/// The settings dialog's save-gating validation, extracted from the view. Covers the
/// timezone/range checks, the non-blank-name/font and uniqueness rules, the
/// at-least-one-style/has-default invariant, and the trimmed-name consistency fix.
/// </summary>
public sealed class SettingsValidatorTests
{
    private static TextStyleDraft Style(
        string name = "Default", string font = "Menlo", double size = 14, double line = 1.4, double pad = 12)
        => new(name, font, size, line, pad);

    private static SettingsDraft Draft(
        IReadOnlyList<TextStyleDraft>? styles = null, string tz = "UTC", double autosave = 2, bool hasDefault = true)
        => new(tz, autosave, styles ?? new[] { Style() }, hasDefault);

    [Fact]
    public void Valid_draft_passes() => Assert.True(SettingsValidator.IsValid(Draft()));

    [Fact]
    public void Unresolvable_timezone_fails() => Assert.False(SettingsValidator.IsValid(Draft(tz: "Not/AZone")));

    [Theory]
    [InlineData(0.24)] // below the 0.25 minimum
    [InlineData(60.1)] // above the 60 maximum
    public void Autosave_out_of_range_fails(double seconds) =>
        Assert.False(SettingsValidator.IsValid(Draft(autosave: seconds)));

    [Fact]
    public void No_styles_fails() => Assert.False(SettingsValidator.IsValid(Draft(styles: new List<TextStyleDraft>())));

    [Fact]
    public void No_default_fails() => Assert.False(SettingsValidator.IsValid(Draft(hasDefault: false)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_fails(string name) =>
        Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(name: name) })));

    [Fact]
    public void Blank_font_fails() => Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(font: " ") })));

    [Theory]
    [InlineData(7)] // font below 8
    [InlineData(49)] // font above 48
    public void Font_size_out_of_range_fails(double size) =>
        Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(size: size) })));

    [Fact]
    public void Duplicate_names_are_a_case_insensitive_conflict() =>
        Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(name: "Foo"), Style(name: "foo") })));

    [Fact]
    public void Names_are_compared_after_trimming_for_both_blank_and_uniqueness()
    {
        // The fix: the blank check and the uniqueness check key off the SAME trimmed name.
        // "  Foo  " and "foo" collide case-insensitively once trimmed -> invalid.
        Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(name: "  Foo  "), Style(name: "foo") })));
        // Trailing whitespace alone is not a real difference: "Foo" and "Foo " collide.
        Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(name: "Foo"), Style(name: "Foo ") })));
        // A whitespace-only name trims to empty -> blank -> invalid.
        Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(name: "   ") })));
        // Distinct trimmed names are fine.
        Assert.True(SettingsValidator.IsValid(Draft(new[] { Style(name: " Foo "), Style(name: "Bar") })));
    }

    [Fact]
    public void UniqueName_returns_the_base_when_free() =>
        Assert.Equal("New style", SettingsValidator.UniqueName("New style", new[] { "Default", "Mono" }));

    [Fact]
    public void UniqueName_appends_a_counter_case_insensitively()
    {
        Assert.Equal("Mono 2", SettingsValidator.UniqueName("Mono", new[] { "mono" }));
        Assert.Equal("Mono 3", SettingsValidator.UniqueName("Mono", new[] { "Mono", "mono 2" }));
    }

    [Fact]
    public void IsDirty_compares_by_canonical_json()
    {
        var a = new AppConfig();
        var b = new AppConfig();
        Assert.False(SettingsValidator.IsDirty(a, b));
        b.AutosaveDelaySeconds = 5;
        Assert.True(SettingsValidator.IsDirty(a, b));
    }
}
