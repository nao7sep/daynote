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
}
