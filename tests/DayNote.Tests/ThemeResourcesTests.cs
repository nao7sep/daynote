using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using DayNote.Core.Configuration;
using DayNote.ViewModels;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests;

public sealed class ThemeResourcesTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData(ThemePreference.System, "Default")]
    [InlineData(ThemePreference.Light, "Light")]
    [InlineData(ThemePreference.Dark, "Dark")]
    public void EachPreferenceMapsToOneThemeVariant(ThemePreference preference, string variant) =>
        Assert.Equal(variant, AppTheme.VariantFor(preference).Key.ToString());

    [Fact]
    public void ANewConfigStartsOnSystemAndWritesTheNameLowercase()
    {
        var config = new AppConfig();
        Assert.Equal(ThemePreference.System, config.Theme);
        config.Theme = ThemePreference.Dark;
        Assert.Contains("\"theme\": \"dark\"", JsonSerializer.Serialize(config, DayNoteJson.Options));
    }

    [Theory]
    [InlineData("{}", ThemePreference.System)]
    [InlineData("{\"theme\":\"light\"}", ThemePreference.Light)]
    [InlineData("{\"theme\":\"Dark\"}", ThemePreference.Dark)]
    [InlineData("{\"theme\":\"sepia\"}", ThemePreference.System)]
    [InlineData("{\"theme\":\"2\"}", ThemePreference.System)]
    [InlineData("{\"theme\":2}", ThemePreference.System)]
    [InlineData("{\"theme\":null}", ThemePreference.System)]
    public void AMissingOrUnrecognizedThemeReadsAsSystem(string json, ThemePreference expected) =>
        Assert.Equal(expected, JsonSerializer.Deserialize<AppConfig>(json, DayNoteJson.Options)!.Theme);

    [Fact]
    public void LightAndDarkDefineTheSameThemedBrushes()
    {
        var light = ThemeBrushes("Light");
        var dark = ThemeBrushes("Dark");
        Assert.NotEmpty(light);
        Assert.Equal(light.Keys.OrderBy(key => key), dark.Keys.OrderBy(key => key));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void TextKeepsHighContrastInEachTheme(string theme)
    {
        var b = ThemeBrushes(theme);
        var failures = new List<string>();
        void Check(string ink, string surface, double floor)
        {
            var ratio = Contrast(b[ink], b[surface]);
            if (ratio < floor)
                failures.Add($"{ink} on {surface}: {ratio:F2}");
        }

        var rows = new[] { "ListBackgroundBrush", "ListSelectionBrush", "ListSelectionHoverBrush" };
        var utility = new[] { "UtilityBrush", "UtilityHoverBrush", "UtilityPressedBrush" };
        foreach (var surface in new[] { "AppBackgroundBrush", "SurfaceBrush" }.Concat(utility).Concat(rows))
            Check("TextPrimaryBrush", surface, 4.5);
        foreach (var surface in new[] { "AppBackgroundBrush", "SurfaceBrush", "UtilityBrush" }.Concat(rows))
            Check("TextSecondaryBrush", surface, 4.5);
        // A note row's lifecycle label on the list, selected or not; error text wherever it appears.
        foreach (var status in new[] { "StatusDraftBrush", "StatusReadyBrush", "StatusPublishedBrush", "StatusExpiredBrush" })
            foreach (var row in rows)
                Check(status, row, 4.5);
        foreach (var surface in new[] { "AppBackgroundBrush", "SurfaceBrush" })
            Check("DangerTextBrush", surface, 4.5);
        foreach (var fill in new[] { "AccentBrush", "AccentHoverBrush", "AccentPressedBrush" })
            Check("AccentForegroundBrush", fill, 4.5);
        // Marks: a field's outline, the accent focus and drop border, the save-state dot, result frames.
        foreach (var surface in new[] { "AppBackgroundBrush", "SurfaceBrush", "ListBackgroundBrush" })
            foreach (var mark in new[] { "FieldBorderBrush", "TextControlBorderBrush", "AccentBrush", "PositiveBrush", "WarningBrush", "DangerTextBrush" })
                Check(mark, surface, 3);

        Assert.True(failures.Count == 0, $"{theme}: {string.Join("; ", failures)}");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ResultCardsStandApartFromTheWorkspaceAndStayReadable(string theme)
    {
        // Each severity's card fill and edge, as MainWindow's Border.shellResult styles pair them.
        var cards = new[]
        {
            ("ResultInfoBackgroundBrush", "AccentBrush"),
            ("ResultWarningBackgroundBrush", "WarningBrush"),
            ("ResultErrorBackgroundBrush", "DangerTextBrush"),
        };
        // Everything a card can float over: the gaps between panes, pane and list surfaces, and rows.
        var workspace = new[]
        {
            "AppBackgroundBrush", "SurfaceBrush", "ListBackgroundBrush",
            "ListSelectionBrush", "ListSelectionHoverBrush", "UtilityBrush",
        };
        var b = ThemeBrushes(theme);
        var failures = new List<string>();
        void Check(string ink, string surface, double floor)
        {
            var ratio = Contrast(b[ink], b[surface]);
            if (ratio < floor)
                failures.Add($"{ink} on {surface}: {ratio:F2}");
        }

        foreach (var (fill, edge) in cards)
        {
            // The message, and the close mark at rest and under the pointer.
            Check("TextPrimaryBrush", fill, 4.5);
            Check("TextSecondaryBrush", fill, 4.5);
            // The edge outlines the card against its own fill and against whatever lies beneath it.
            Check(edge, fill, 3);
            foreach (var surface in workspace)
                Check(edge, surface, 3);
        }

        Assert.True(failures.Count == 0, $"{theme}: {string.Join("; ", failures)}");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void WhiteLabelsKeepHighContrastOnTheDangerFill(string theme)
    {
        var b = ThemeBrushes(theme);
        foreach (var fill in new[] { "DangerBrush", "DangerHoverBrush", "DangerPressedBrush" })
            Assert.True(Contrast(Colors.White, b[fill]) >= 4.5, $"{theme}: white on {fill}");
    }

    [Fact]
    public void FluentPalettesRepeatTheThemeAccentRegionAndErrorText()
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var palette = AppXaml().Descendants()
                .Single(element => element.Name.LocalName == "ColorPaletteResources" && (string?)element.Attribute(X + "Key") == theme);
            var b = ThemeBrushes(theme);
            Assert.Equal(b["AccentBrush"], Color.Parse((string)palette.Attribute("Accent")!));
            Assert.Equal(b["AppBackgroundBrush"], Color.Parse((string)palette.Attribute("RegionColor")!));
            Assert.Equal(b["DangerTextBrush"], Color.Parse((string)palette.Attribute("ErrorText")!));
        }
    }

    [AvaloniaFact]
    public void CodeBuiltAndMarkupSurfacesRepaintWhenTheThemeChanges()
    {
        var app = Application.Current!;
        var dialog = new ShortcutsDialog(ShortcutCatalog.Build(new Window()));
        try
        {
            AppTheme.Apply(ThemePreference.Light);
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            var light = CardBackgrounds(dialog);

            AppTheme.Apply(ThemePreference.Dark);
            Dispatcher.UIThread.RunJobs();
            var dark = CardBackgrounds(dialog);

            Assert.Equal(ThemeBrushes("Light")["SurfaceBrush"], Assert.Single(light.Distinct()));
            Assert.Equal(ThemeBrushes("Dark")["SurfaceBrush"], Assert.Single(dark.Distinct()));
            Assert.Equal(ThemeBrushes("Dark")["AppBackgroundBrush"], ((ISolidColorBrush)dialog.Background!).Color);
        }
        finally
        {
            dialog.Close();
            app.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public void SettingsOffersTheThreeThemesAsOneRadioGroupAppliedOnlyBySave()
    {
        AppConfig? saved = null;
        var dialog = new SettingsDialog(new AppConfig { Theme = ThemePreference.Dark }, candidate =>
        {
            saved = candidate;
            return true;
        });
        var radios = dialog.GetLogicalDescendants().OfType<RadioButton>().ToList();
        Assert.Equal(new[] { "System", "Light", "Dark" }, radios.Select(radio => (string)radio.Content!));
        Assert.Single(radios.Select(radio => radio.Parent).Distinct());
        Assert.Equal("Dark", radios.Single(radio => radio.IsChecked == true).Content);

        radios[1].IsChecked = true;
        Assert.Null(saved);
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));
        Assert.True(save.IsEnabled);
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ThemePreference.Light, saved!.Theme);
    }

    [Fact]
    public void ResultsExposeSeverityNotBrushes()
    {
        Assert.True(new OperationResultViewModel(OperationResultKind.Warning, "w").IsWarning);
        Assert.True(new OperationResultViewModel(OperationResultKind.Error, "e").IsError);
        var info = new OperationResultViewModel(OperationResultKind.Info, "i");
        Assert.False(info.IsWarning || info.IsError);
    }

    private static List<Color> CardBackgrounds(Window dialog) =>
        dialog.GetLogicalDescendants().OfType<Border>()
            .Where(border => border.CornerRadius == new CornerRadius(8) && border.Background is not null)
            .Select(border => ((ISolidColorBrush)border.Background!).Color)
            .ToList();

    private static XDocument AppXaml() =>
        XDocument.Load(Path.Combine(RepoRoot(), "src", "DayNote", "App.axaml"));

    private static Dictionary<string, Color> ThemeBrushes(string theme) =>
        AppXaml().Descendants()
            .Single(element => element.Name.LocalName == "ResourceDictionary"
                && (string?)element.Attribute(X + "Key") == theme)
            .Elements()
            .Where(element => element.Name.LocalName == "SolidColorBrush")
            .ToDictionary(
                element => (string)element.Attribute(X + "Key")!,
                element => Color.Parse((string)element.Attribute("Color")!));

    private static string RepoRoot([CallerFilePath] string callerPath = "")
    {
        // This file: <repo>/tests/DayNote.Tests/ThemeResourcesTests.cs
        var testsProjectDir = Path.GetDirectoryName(callerPath)!;
        return Path.GetFullPath(Path.Combine(testsProjectDir, "..", ".."));
    }

    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
