using DayNote.Core.Configuration;
using System.Text.Json;
using Xunit;

namespace DayNote.Tests.Configuration;

public sealed class WindowPlacementPolicyTests
{
    private static readonly DisplayWorkArea[] Displays =
    [
        new(0, 0, 1920, 1080, 1),
        new(-2560, 0, 2560, 2048, 2),
    ];

    [Fact]
    public void Missing_state_defaults_maximized()
    {
        var result = WindowPlacementPolicy.Resolve(null, Displays);
        Assert.Equal("maximized", result.Mode);
        Assert.Null(result.NormalBounds);
    }

    [Fact]
    public void Partial_and_below_minimum_bounds_are_available_for_toolkit_adjustment()
    {
        foreach (var bounds in new[]
        {
            new WindowBounds { X = 1200, Y = 10, Width = 900, Height = 700 },
            new WindowBounds { X = 10, Y = 10, Width = 300, Height = 200 },
            new WindowBounds { X = -2600, Y = 20, Width = 1900, Height = 1300 },
        })
        {
            Assert.Same(bounds, WindowPlacementPolicy.Resolve(
                new WindowPlacement { NormalBounds = bounds }, Displays).NormalBounds);
        }
    }

    [Fact]
    public void Nonpositive_bounds_are_discarded_without_losing_mode()
    {
        var result = WindowPlacementPolicy.Resolve(new WindowPlacement
        {
            Mode = "maximized",
            NormalBounds = new WindowBounds { Width = 0, Height = 700 },
        }, Displays);
        Assert.Null(result.NormalBounds);
        Assert.Equal("maximized", result.Mode);
    }

    [Fact]
    public void Invalid_geometry_falls_back_without_losing_stable_mode()
    {
        var result = WindowPlacementPolicy.Resolve(new WindowPlacement
        {
            NormalBounds = new WindowBounds { X = 9000, Y = 9000, Width = 1200, Height = 800 },
            Mode = "normal",
        }, Displays);
        Assert.Equal("normal", result.Mode);
        Assert.Null(result.NormalBounds);
    }

    [Fact]
    public void Malformed_mode_uses_the_declared_default()
    {
        var result = WindowPlacementPolicy.Resolve(new WindowPlacement { Mode = "fullscreen" }, Displays);
        Assert.Equal("maximized", result.Mode);
    }

    [Fact]
    public void Malformed_placement_self_heals_without_resetting_sibling_state()
    {
        var state = JsonSerializer.Deserialize<AppState>("""
            {"bindersPaneWidth":333,"windowPlacements":{"main":{"normalBounds":{"x":1,"y":2,"width":"wide","height":700},"mode":"fullscreen"}}}
            """, DayNoteJson.Options)!;
        Assert.Equal(333, state.BindersPaneWidth);
        Assert.Null(state.WindowPlacements.Main!.NormalBounds);
        Assert.Equal("maximized", state.WindowPlacements.Main.Mode);
    }
}
