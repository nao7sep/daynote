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
        var result = WindowPlacementPolicy.Resolve(null, 900, 600, Displays);
        Assert.Equal("maximized", result.Mode);
        Assert.Null(result.NormalBounds);
    }

    [Fact]
    public void Bounds_must_meet_scaled_minimum_and_fit_wholly_on_one_display()
    {
        Assert.True(WindowPlacementPolicy.IsUsable(
            new WindowBounds { X = -2400, Y = 20, Width = 1900, Height = 1300 }, 900, 600, Displays));
        Assert.False(WindowPlacementPolicy.IsUsable(
            new WindowBounds { X = 10, Y = 10, Width = 899, Height = 700 }, 900, 600, Displays));
        Assert.False(WindowPlacementPolicy.IsUsable(
            new WindowBounds { X = 1200, Y = 10, Width = 900, Height = 700 }, 900, 600, Displays));
    }

    [Fact]
    public void Invalid_geometry_falls_back_without_losing_stable_mode()
    {
        var result = WindowPlacementPolicy.Resolve(new WindowPlacement
        {
            NormalBounds = new WindowBounds { X = 9000, Y = 9000, Width = 1200, Height = 800 },
            Mode = "normal",
        }, 900, 600, Displays);
        Assert.Equal("normal", result.Mode);
        Assert.Null(result.NormalBounds);
    }

    [Fact]
    public void Malformed_mode_uses_the_declared_default()
    {
        var result = WindowPlacementPolicy.Resolve(new WindowPlacement { Mode = "fullscreen" }, 900, 600, Displays);
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
