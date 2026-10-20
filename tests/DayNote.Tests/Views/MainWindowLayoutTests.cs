using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DayNote.Tests.I18n;
using DayNote.Tests.Storage;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The window's minimum size comes from its panes' minimums: at the narrowest and shortest window,
/// every pane gets exactly the room its column asks for and nothing is cut off.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class MainWindowLayoutTests : WindowTest
{
    private const double Tolerance = 1.0;

    [AvaloniaFact]
    public void At_the_minimum_size_every_pane_gets_its_own_minimum()
    {
        using var populated = PopulatedMainWindow.Empty();
        var window = Show(populated.Window);
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Dispatcher.UIThread.RunJobs();

        var grid = window.FindControl<Grid>("PaneGrid")!;
        var panes = grid.ColumnDefinitions.Where(column => column.MinWidth > 0).ToArray();
        Assert.Equal(4, panes.Length);
        foreach (var column in panes)
            Assert.InRange(column.ActualWidth, column.MinWidth - Tolerance, column.MinWidth + Tolerance);

        Assert.True(grid.Bounds.Width <= window.Bounds.Width + Tolerance);
        var editor = window.FindControl<Border>("EditorPane")!;
        Assert.True(editor.Bounds.Bottom <= grid.Bounds.Height + Tolerance);
    }
}
