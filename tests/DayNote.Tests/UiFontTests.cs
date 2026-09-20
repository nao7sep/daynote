using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DayNote;
using DayNote.Core.Configuration;
using Xunit;

namespace DayNote.Tests;

/// <summary>
/// The UI-font resolver turns the free-text (possibly comma-separated) chrome-font setting into a
/// concrete family: the first installed family wins, otherwise the bundled default (Inter).
/// </summary>
public sealed class UiFontTests
{
    [Fact]
    public void ParseFamilies_splits_trims_strips_quotes_and_drops_empties()
    {
        Assert.Equal(
            new[] { "Helvetica Neue", "Segoe UI", "Roboto" },
            UiFont.ParseFamilies("\"Helvetica Neue\", Segoe UI , , 'Roboto'").ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseFamilies_yields_nothing_for_a_blank_value(string? value)
    {
        Assert.Empty(UiFont.ParseFamilies(value));
    }

    [AvaloniaFact]
    public void Resolve_falls_back_to_the_bundled_default_when_nothing_matches()
    {
        Assert.Equal(AppConfig.DefaultUiFontFamily, UiFont.Resolve("No Such Font 99999").Name);
        Assert.Equal(AppConfig.DefaultUiFontFamily, UiFont.Resolve("").Name);
        Assert.Equal(AppConfig.DefaultUiFontFamily, UiFont.Resolve(null).Name);
    }

    [AvaloniaFact]
    public void Resolve_prefers_the_first_installed_family()
    {
        var installed = FontManager.Current.SystemFonts.FirstOrDefault();
        if (installed is null)
        {
            // No system fonts in this environment; the fallback path is covered above.
            return;
        }

        // An absent family listed first is skipped in favor of the installed one.
        Assert.Equal(installed.Name, UiFont.Resolve($"No Such Font 99999, {installed.Name}").Name);
    }

    // The localized family names Avalonia does not expose, which only the platform matcher knows.
    [AvaloniaTheory]
    [InlineData("メイリオ", "Meiryo")]
    [InlineData("ヒラギノ角ゴシック", "Hiragino Sans")]
    [InlineData("游明朝体", "YuMincho")]
    [InlineData("游ゴシック体", "YuGothic")]
    [InlineData("ヒラギノ明朝 ProN", "Hiragino Mincho ProN")]
    public void A_Japanese_family_name_resolves_for_both_ui_and_editor_fonts(string name, string family)
    {
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(family)), out var expected)
            || !IsFamily(expected, family))
        {
            Assert.Skip($"{family} is not installed.");
        }

        foreach (var resolved in new[] { UiFont.Resolve(name), UiFont.ResolveEditor(name) })
        {
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(resolved), out var face));
            Assert.True(IsFamily(face, family), $"{name} resolved to {face.FamilyName}.");
        }

        Assert.Equal(name, UiFont.EditorFamilyName(name));
    }

    [AvaloniaFact]
    public void A_name_no_font_declares_falls_back_every_time_it_is_looked_up()
    {
        // 遊 (U+904A) is what an IME usually gives for ゆう; the installed font is 游明朝体 (U+6E38).
        // Avalonia lists every name ever requested, so a second lookup must not find the first one.
        const string misspelled = "遊明朝体";
        var fixedWidth = UiFont.ResolveEditor(EditorTextStyle.DefaultFixedWidthFamilies).Name;

        for (var lookup = 0; lookup < 3; lookup++)
        {
            Assert.Equal(fixedWidth, UiFont.ResolveEditor(misspelled).Name);
            Assert.Equal(AppConfig.DefaultUiFontFamily, UiFont.Resolve(misspelled).Name);
            Assert.NotEqual(misspelled, UiFont.EditorFamilyName(misspelled));
        }
    }

    [AvaloniaFact]
    public void Inter_always_means_the_bundled_font_with_its_bold_weight()
    {
        foreach (var family in new[] { UiFont.Resolve("Inter"), UiFont.ResolveEditor("inter") })
        {
            Assert.True(FontManager.Current.TryGetGlyphTypeface(
                new Typeface(family, FontStyle.Normal, FontWeight.Bold), out var face));
            Assert.Equal("Inter", face.FamilyName);
        }
    }

    private static bool IsFamily(GlyphTypeface face, string family) =>
        face.TypographicFamilyName == family || face.FamilyName == family;
}
