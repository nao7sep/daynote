using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The window's minimum size is derived, not guessed (per the window-chrome conventions):
/// <see cref="WindowMetrics"/> sums the live pane-Grid column minimums plus the splitters, grid
/// margin and fixed chrome so the window can never shrink small enough to hide a pane, toolbar, or
/// status bar. The bounded result viewport is an overlay and contributes no layout height. These tests pin the derivation math directly
/// (no Avalonia headless harness, matching the suite's pure-helper style) and guard that every
/// content column declares a non-zero minimum width — so a future column added without one fails
/// here rather than silently letting the window under-size.
/// </summary>
public sealed class WindowMetricsTests
{
    [Theory]
    [InlineData(false, false, Avalonia.Controls.WindowState.Normal)]
    [InlineData(true, false, Avalonia.Controls.WindowState.Normal)]
    [InlineData(false, true, Avalonia.Controls.WindowState.Normal)]
    [InlineData(true, true, Avalonia.Controls.WindowState.Maximized)]
    public void Restored_window_state_is_maximized_only_on_Windows(
        bool maximized, bool isWindows, Avalonia.Controls.WindowState expected)
    {
        Assert.Equal(expected, WindowMetrics.RestoredWindowState(maximized, isWindows));
    }

    [Theory]
    [InlineData(1280, 720, 2560, 1280, 1, false)]
    [InlineData(2557, 1276, 2560, 1280, 1, true)]
    [InlineData(1278, 636, 2560, 1280, 2, true)]
    [InlineData(2560, 1280, 2560, 1280, 0, false)]
    public void Maximized_geometry_matches_the_scaled_working_area(
        double width, double height, int workWidth, int workHeight, double scale, bool expected)
    {
        Assert.Equal(expected, WindowMetrics.IsMaximizedGeometry(
            new Avalonia.Size(width, height),
            new Avalonia.PixelRect(0, 30, workWidth, workHeight),
            scale));
    }

    [Fact]
    public void Saved_window_geometry_requires_a_position_on_a_current_working_area()
    {
        Avalonia.PixelRect[] workingAreas =
        [
            new(-1920, -200, 1920, 1080),
            new(0, 0, 2560, 1440),
        ];

        Assert.True(WindowMetrics.CanRestoreWindowGeometry(-1800, -100, 1200, 800, workingAreas));
        Assert.True(WindowMetrics.CanRestoreWindowGeometry(0, 0, 1200, 800, workingAreas));
        Assert.False(WindowMetrics.CanRestoreWindowGeometry(-2500, 100, 1200, 800, workingAreas));
        Assert.False(WindowMetrics.CanRestoreWindowGeometry(2560, 100, 1200, 800, workingAreas));
    }

    [Theory]
    [InlineData(null, 0, 1200.0, 800.0)]
    [InlineData(0, null, 1200.0, 800.0)]
    [InlineData(0, 0, null, 800.0)]
    [InlineData(0, 0, 1200.0, null)]
    [InlineData(0, 0, 0.0, 800.0)]
    [InlineData(0, 0, 1200.0, -1.0)]
    [InlineData(0, 0, double.PositiveInfinity, 800.0)]
    public void Saved_window_geometry_rejects_missing_or_invalid_primitives(
        int? x, int? y, double? width, double? height)
    {
        Assert.False(WindowMetrics.CanRestoreWindowGeometry(
            x, y, width, height, [new Avalonia.PixelRect(0, 0, 1920, 1080)]));
    }

    [Fact]
    public void Native_minimum_uses_scaled_work_area_without_changing_the_content_floor()
    {
        var floor = new Avalonia.Size(1200, 800);
        var capped = WindowMetrics.CapMinimumToWorkArea(
            floor, new Avalonia.PixelRect(-1920, 40, 1600, 1000), 2, new Avalonia.Size(8, 30));
        Assert.Equal(new Avalonia.Size(792, 470), capped);
        Assert.Equal(new Avalonia.Size(1200, 800), floor);
        Assert.Equal(floor, WindowMetrics.CapMinimumToWorkArea(
            floor, new Avalonia.PixelRect(0, 0, 3840, 2160), 2, new Avalonia.Size(8, 30)));
    }

    // Mirrors the four content-column minimums declared in Views/MainWindow.axaml (the splitter
    // columns are Auto with no minimum). Kept here so the derivation assertion reads against a
    // concrete, known set; the separate axaml guard below catches drift between this list and the
    // actual XAML.
    private static readonly double[] ContentColumnMinWidths = [150, 170, 320, 170];

    // Chrome budget added on top of the columns: three GridSplitters at Width="6" plus the pane
    // Grid's 4px left+right margin. Mirrors the private constants in WindowMetrics.
    private const double ChromeWidth = (6 * 3) + (4 + 4);

    // The editor pane is the tallest; its content MinHeight in the XAML drives the height.
    private const double TallestPaneMinHeight = 300;

    [Fact]
    public void MinWidth_EqualsColumnMinimumsPlusChrome()
    {
        var expected = ContentColumnMinWidths.Sum() + ChromeWidth;
        Assert.Equal(expected, WindowMetrics.MinWidthFor(ContentColumnMinWidths));
    }

    [Fact]
    public void MinWidth_TracksTheColumnsItIsGiven()
    {
        // Adding a column to the input must move the derived minimum by exactly that column's
        // minimum width — the property that keeps the window and its columns from drifting apart.
        var baseWidth = WindowMetrics.MinWidthFor(ContentColumnMinWidths);
        var widened = WindowMetrics.MinWidthFor([.. ContentColumnMinWidths, 200]);
        Assert.Equal(baseWidth + 200, widened);
    }

    [Fact]
    public void MinHeight_EqualsChromePlusTallestPaneMinimum()
    {
        // Toolbar (52) + status bar (33) + pane vertical margins (16) + the tallest pane's content
        // minimum. The result viewport overlays the panes and contributes no layout reserve.
        const double chromeHeight = 52 + 33 + 16;
        Assert.Equal(
            chromeHeight + TallestPaneMinHeight,
            WindowMetrics.MinHeightFor(TallestPaneMinHeight));
    }

    [Fact]
    public void MinHeight_TracksTheTallestPaneMinimum()
    {
        // Raising the tallest pane's content minimum must raise the window minimum by the same
        // amount — the height counterpart to the width-tracking property above.
        var baseHeight = WindowMetrics.MinHeightFor(TallestPaneMinHeight);
        Assert.Equal(
            baseHeight + 50,
            WindowMetrics.MinHeightFor(TallestPaneMinHeight + 50));
    }

    [Fact]
    public void EveryContentColumn_DeclaresANonZeroMinWidth()
    {
        // Guard against a content column being added without a MinWidth: such a column would
        // contribute 0 to the derived window minimum and could be squeezed to invisibility. Read
        // the live XAML so this fails the moment a real column is added without a minimum. The
        // splitter columns are intentionally Auto with no MinWidth and are excluded by the regex.
        var minWidths = ColumnMinWidths(ReadMainWindowAxaml());

        Assert.NotEmpty(minWidths);
        Assert.All(minWidths, m => Assert.True(m > 0, "A pane column is missing a non-zero MinWidth."));
    }

    [Fact]
    public void DerivedMinWidth_MatchesTheLiveColumnMinimums()
    {
        // The mirrored ContentColumnMinWidths used above must stay equal to what the XAML actually
        // declares, so the derivation test cannot pass against a stale list.
        var fromXaml = ColumnMinWidths(ReadMainWindowAxaml());
        Assert.Equal(ContentColumnMinWidths, fromXaml);
    }

    private static IReadOnlyList<double> ColumnMinWidths(string axaml) =>
        Regex.Matches(axaml, "<ColumnDefinition\\b[^>]*?MinWidth=\"(?<min>\\d+(?:\\.\\d+)?)\"")
            .Select(m => double.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture))
            .ToList();

    // --- Side-pane distribution (the fill/pixel rule) ---

    private static readonly double[] SideMins = [150, 170, 170];
    private static readonly double[] DefaultIntents = [220, 260, 260];

    [Fact]
    public void SidePaneBudget_SubtractsEditorMinAndChrome()
    {
        Assert.Equal(1200 - 320 - ChromeWidth, WindowMetrics.SidePaneBudget(1200, 320));
    }

    [Fact]
    public void DistributeSidePanes_AllIntentsFit_ReturnsIntentsUnchanged()
    {
        var budget = 900.0;
        var result = WindowMetrics.DistributeSidePanes(DefaultIntents, SideMins, budget);

        Assert.Equal(DefaultIntents, result);
    }

    [Fact]
    public void DistributeSidePanes_NarrowWindow_ShrinksPanesProportionally()
    {
        var budget = SideMins.Sum() + 100;
        var result = WindowMetrics.DistributeSidePanes(DefaultIntents, SideMins, budget);

        for (var i = 0; i < result.Length; i++)
            Assert.True(result[i] >= SideMins[i], $"Pane {i} went below its minimum.");
        Assert.Equal(budget, result.Sum(), precision: 6);
    }

    [Fact]
    public void DistributeSidePanes_AtMinimumBudget_AllPanesAtMinimum()
    {
        var budget = SideMins.Sum();
        var result = WindowMetrics.DistributeSidePanes(DefaultIntents, SideMins, budget);

        Assert.Equal(SideMins, result);
    }

    [Fact]
    public void DistributeSidePanes_BudgetBelowMinimums_FloorsToMinimums()
    {
        var result = WindowMetrics.DistributeSidePanes(DefaultIntents, SideMins, budget: 100);

        Assert.Equal(SideMins, result);
    }

    [Fact]
    public void DistributeSidePanes_IntentsBelowMinimums_ClampsUp()
    {
        double[] lowIntents = [100, 100, 100];
        var result = WindowMetrics.DistributeSidePanes(lowIntents, SideMins, budget: 900);

        Assert.Equal(SideMins, result);
    }

    [Fact]
    public void DistributeSidePanes_ProportionalShrinkPreservesRatios()
    {
        double[] intents = [250, 350, 250];
        var budget = SideMins.Sum() + 50;
        var result = WindowMetrics.DistributeSidePanes(intents, SideMins, budget);

        var excess0 = intents[0] - SideMins[0];
        var excess1 = intents[1] - SideMins[1];
        var excess2 = intents[2] - SideMins[2];
        var totalExcess = excess0 + excess1 + excess2;

        var slack0 = result[0] - SideMins[0];
        var slack1 = result[1] - SideMins[1];
        var slack2 = result[2] - SideMins[2];

        Assert.Equal(excess0 / totalExcess, slack0 / 50, precision: 6);
        Assert.Equal(excess1 / totalExcess, slack1 / 50, precision: 6);
        Assert.Equal(excess2 / totalExcess, slack2 / 50, precision: 6);
    }

    private static string ReadMainWindowAxaml([CallerFilePath] string callerPath = "")
    {
        var testsViewsDir = Path.GetDirectoryName(callerPath)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testsViewsDir, "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repoRoot, "src", "DayNote", "Views", "MainWindow.axaml"));
    }
}
