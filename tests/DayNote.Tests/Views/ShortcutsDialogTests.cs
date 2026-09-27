using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class ShortcutsDialogTests : WindowTest
{
    [Theory]
    [InlineData(new[] { 4, 3, 2, 1, 3 }, 2)] // 7 | 6
    [InlineData(new[] { 1, 1 }, 1)]
    [InlineData(new[] { 5, 1, 1, 1 }, 1)] // 5 | 3 beats 6 | 2
    [InlineData(new[] { 3 }, 0)] // one section: either side holds 3, the earlier split wins
    [InlineData(new int[0], 0)]
    public void Sections_split_where_the_taller_column_is_shortest(int[] rows, int expected) =>
        Assert.Equal(expected, ShortcutsDialog.BalancedSplit(rows));

    [AvaloniaFact]
    public void The_dialog_fits_within_the_main_windows_default_height()
    {
        var owner = Show(new Window());
        var dialog = Show(new ShortcutsDialog(ShortcutCatalog.Build(owner)));
        dialog.UpdateLayout();

        // The main window opens at 1200×800; its shortcuts should be readable without scrolling there.
        Assert.True(dialog.DesiredSize.Height < 800, $"The dialog wants {dialog.DesiredSize.Height:0} px.");
    }

    // A reference list carries no card around a group and no rule between rows: the only drawn edge
    // on the surface is each keycap's (interface-styling conventions).
    [AvaloniaFact]
    public void Only_the_keycaps_draw_an_edge()
    {
        var owner = Show(new Window());
        var dialog = Show(new ShortcutsDialog(ShortcutCatalog.Build(owner)));
        var content = dialog.FindControl<ContentPresenter>("DialogContent")!;

        var framed = content.GetVisualDescendants().OfType<Border>()
            .Where(border => border.BorderThickness != default || border.Height == 1)
            .ToList();
        Assert.NotEmpty(framed);
        Assert.All(framed, border => Assert.Contains("keycap", border.Classes));
    }
}
