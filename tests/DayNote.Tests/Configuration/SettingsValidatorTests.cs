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
    public void System_timezone_passes() => Assert.True(SettingsValidator.IsValid(Draft(tz: "system")));

    [Fact]
    public void Unresolvable_timezone_fails() => Assert.False(SettingsValidator.IsValid(Draft(tz: "Not/AZone")));

    [Theory]
    [InlineData(SettingsValidator.MinAutosaveSeconds - 0.01)]
    [InlineData(SettingsValidator.MaxAutosaveSeconds + 0.1)]
    public void Autosave_out_of_range_fails(double seconds) =>
        Assert.False(SettingsValidator.IsValid(Draft(autosave: seconds)));

    [Fact]
    public void No_styles_fails() => Assert.False(SettingsValidator.IsValid(Draft(styles: new List<TextStyleDraft>())));

    [Fact]
    public void No_default_fails() => Assert.False(SettingsValidator.IsValid(Draft(hasDefault: false)));

    [Fact]
    public void Blank_font_fails() => Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(font: " ") })));

    [Theory]
    [InlineData(SettingsValidator.MinFontSize - 1)]
    [InlineData(SettingsValidator.MaxFontSize + 1)]
    public void Font_size_out_of_range_fails(double size) =>
        Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(size: size) })));

    [Fact]
    public void Two_presets_with_the_same_family_are_allowed() =>
        Assert.True(SettingsValidator.IsValid(Draft(new[] { Style(size: 14), Style(size: 18) })));

    [Theory]
    [InlineData(14.5, 1.4, 12.0)] // a font size must be whole
    [InlineData(14.0, 1.4, 12.5)] // and so must the padding
    [InlineData(14.0, 0.9, 12.0)]
    [InlineData(14.0, 3.1, 12.0)]
    [InlineData(14.0, 1.4, 49.0)]
    public void Each_style_number_is_checked_as_its_field_reads_it(double size, double line, double pad) =>
        Assert.False(SettingsValidator.IsValid(Draft(new[] { Style(size: size, line: line, pad: pad) })));

    [Theory]
    [InlineData("12", 12.0)]
    [InlineData(" 12 ", 12.0)]
    [InlineData("1.5", null)]   // not whole
    [InlineData("49", null)]    // out of range
    [InlineData("", null)]
    [InlineData("1e1", null)]   // no exponents
    [InlineData("1,000", null)] // no group separators
    [InlineData("abc", null)]
    public void A_whole_range_reads_only_whole_numbers_inside_it(string text, double? expected) =>
        Assert.Equal(expected, SettingsValidator.FontSize.Read(text, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("en", "1.5", 1.5)]
    [InlineData("de", "1,5", 1.5)]
    [InlineData("de", "1.5", 1.5)] // a point is never a group separator here
    [InlineData("fr", "2,25", 2.25)]
    [InlineData("en", "0.9", null)]
    public void A_number_reads_in_the_interface_language_or_with_a_point(string culture, string text, double? expected) =>
        Assert.Equal(expected, SettingsValidator.LineSpacing.Read(text, System.Globalization.CultureInfo.GetCultureInfo(culture)));

    [Theory]
    [InlineData(1.0, -0.1, 1.0)]
    [InlineData(1.4, 0.1, 1.5)]
    [InlineData(2.95, 0.1, 3.0)]
    public void A_step_stays_inside_the_range_without_rounding_drift(double from, double by, double expected) =>
        Assert.Equal(expected, SettingsValidator.LineSpacing.Step(from, by));
}
