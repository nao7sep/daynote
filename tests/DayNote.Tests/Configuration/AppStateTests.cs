using System.Text.Json;
using DayNote.Core.Configuration;
using Xunit;

namespace DayNote.Tests.Configuration;

public sealed class AppStateTests
{
    [Fact]
    public void Obsolete_window_state_is_ignored_and_not_recreated()
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
