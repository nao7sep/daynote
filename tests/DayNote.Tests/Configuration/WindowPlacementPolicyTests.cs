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
    public void Accepted_restored_bounds_seed_the_normal_landing_rectangle()
    {
        var accepted = new WindowBounds { X = 120, Y = 140, Width = 1340, Height = 880 };
        var opening = new WindowBounds { X = 300, Y = 220, Width = 1200, Height = 800 };

        Assert.Same(accepted, WindowPlacementPolicy.SeedNormalBounds(
            new WindowPlacement { NormalBounds = accepted, Mode = "normal" }, opening));
        Assert.Same(opening, WindowPlacementPolicy.SeedNormalBounds(
            new WindowPlacement { NormalBounds = null, Mode = "maximized" }, opening));
    }

    [Fact]
    public void Native_fullscreen_frame_matches_only_the_complete_display()
    {
        var displays = new[] { new DisplayWorkArea(0, 0, 2560, 1440, 1) };
        Assert.True(WindowPlacementPolicy.IsFullDisplayFrame(
            new WindowBounds { X = 0, Y = 0, Width = 2560, Height = 1440 }, displays));
        Assert.False(WindowPlacementPolicy.IsFullDisplayFrame(
            new WindowBounds { X = 0, Y = 30, Width = 2560, Height = 1311 }, displays));
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
