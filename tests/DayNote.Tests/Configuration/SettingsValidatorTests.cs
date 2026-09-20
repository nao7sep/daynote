using System.Collections.Generic;

using DayNote.Core.Configuration;
using Xunit;

namespace DayNote.Tests.Configuration;

/// <summary>
/// The settings dialog's save-gating validation, extracted from the view. Covers the
/// timezone/range checks, the non-blank font rule, and the at-least-one-style/has-default
/// invariant.
/// </summary>
public sealed class SettingsValidatorTests
{
    private static TextStyleDraft Style(string font = "Menlo", double size = 14, double line = 1.4, double pad = 12)
        => new(font, size, line, pad);

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

    [Fact]
    public void Blank_font_fails() => Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(font: " ") })));

    [Theory]
    [InlineData(7)] // font below 8
    [InlineData(49)] // font above 48
    public void Font_size_out_of_range_fails(double size) =>
        Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(size: size) })));

    [Fact]
    public void Two_presets_with_the_same_family_are_allowed() =>
        Assert.True(SettingsValidator.IsValid(Draft(new[] { Style(size: 14), Style(size: 18) })));

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
