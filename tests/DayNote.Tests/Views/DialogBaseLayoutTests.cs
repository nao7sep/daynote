using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class DialogBaseLayoutTests
{
    [AvaloniaFact]
    public void Only_the_body_sits_in_the_vertical_scroll_region()
    {
        var dialog = new MessageDialog(
            "Long message",
            string.Join("\n", Enumerable.Range(0, 400).Select(i => $"line {i}")),
            [new DialogButton("OK", "ok", DialogButtonKind.Primary)]);

        var content = dialog.GetLogicalDescendants()
            .OfType<ContentPresenter>()
            .FirstOrDefault(c => c.Name == "DialogContent");
        Assert.NotNull(content);

        var scroll = content!.GetLogicalAncestors().OfType<ScrollViewer>().Single();
        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);

        var header = dialog.FindControl<TextBlock>("HeaderText");
        var footer = dialog.FindControl<StackPanel>("ButtonPanel");
        Assert.NotNull(header);
        Assert.NotNull(footer);
        Assert.Empty(header!.GetLogicalAncestors().OfType<ScrollViewer>());
        Assert.Empty(footer!.GetLogicalAncestors().OfType<ScrollViewer>());
    }

    [Fact]
    public void Dialog_button_intents_have_explicit_keyboard_focus_states()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DayNote", "App.axaml"));

        Assert.Contains("Button.accent:focus /template/ ContentPresenter", app);
        Assert.Contains("Button.destructive:focus /template/ ContentPresenter", app);
        Assert.Contains("Button.utility:focus /template/ ContentPresenter", app);
    }

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
