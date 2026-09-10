using System.Text.Json;
using DayNote.Core.Configuration;
using Xunit;

namespace DayNote.Tests.Configuration;

public sealed class AppStateTests
{
    [Fact]
    public void Window_geometry_round_trips_as_four_primitives()
    {
        var state = new AppState
        {
            WindowPositionX = -1200,
            WindowPositionY = 80,
            WindowWidth = 1100.5,
            WindowHeight = 720.25,
        };

        var json = JsonSerializer.Serialize(state, DayNoteJson.Options);
        var restored = JsonSerializer.Deserialize<AppState>(json, DayNoteJson.Options)!;

        Assert.Equal(-1200, restored.WindowPositionX);
        Assert.Equal(80, restored.WindowPositionY);
        Assert.Equal(1100.5, restored.WindowWidth);
        Assert.Equal(720.25, restored.WindowHeight);
        Assert.DoesNotContain("windowPlacements", json);
    }

    [Fact]
    public void Obsolete_window_placement_model_is_ignored_and_not_recreated()
    {
        var state = JsonSerializer.Deserialize<AppState>("""
            {
              "bindersPaneWidth": 333,
              "windowPlacements": {
                "main": {
                  "normalBounds": { "x": 1, "y": 2, "width": 1200, "height": 800 },
                  "mode": "maximized"
                }
              }
            }
            """, DayNoteJson.Options)!;

        Assert.Equal(333, state.BindersPaneWidth);
        Assert.DoesNotContain("windowPlacements", JsonSerializer.Serialize(state, DayNoteJson.Options));
    }
}
