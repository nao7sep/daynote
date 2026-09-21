using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Core.Configuration;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The shell's side of the modal-dialog conventions, measured on the real dialogs over a real owner:
/// the bands, the bound, the resize, and the body's clearance from its scroll bar.
/// </summary>
public sealed class DialogBaseLayoutTests : IDisposable
{
    private readonly List<Window> _open = [];

    // Shorter than Settings, so the bound is doing the work. The position matters: an owner at the top
    // of the screen hides a centring mistake, because the placement a too-tall dialog would get is
    // clamped to the screen edge and lands on the right answer by accident.
    private Window ShortOwner()
    {
        var owner = new Window { Width = 900, Height = 420, Position = new PixelPoint(120, 120) };
        _open.Add(owner);
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        return owner;
    }

    private SettingsDialog OpenSettings(Window owner)
    {
        var dialog = new SettingsDialog(new AppConfig(), _ => true);
        _open.Add(dialog);
        _ = dialog.ShowBoundedAsync(owner);
        Dispatcher.UIThread.RunJobs();
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return dialog;
    }

    private static ScrollViewer Body(Window dialog) =>
        dialog.GetVisualDescendants().OfType<ScrollViewer>().Single(v => v.Name == "DialogScroll");

    private static Rect At(Visual visual, Visual relativeTo)
    {
        var topLeft = visual.TranslatePoint(new Point(0, 0), relativeTo)!.Value;
        return new Rect(topLeft.X, topLeft.Y, visual.Bounds.Width, visual.Bounds.Height);
    }

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

        // The OS title bar is the header, so the shell draws no header band of its own; the footer and
        // the line that opens it stay outside the scroll region.
        Assert.Null(dialog.FindControl<TextBlock>("HeaderText"));
        var footer = dialog.FindControl<StackPanel>("ButtonPanel");
        var separator = dialog.FindControl<Border>("FooterSeparator");
        Assert.NotNull(footer);
        Assert.NotNull(separator);
        Assert.Empty(footer!.GetLogicalAncestors().OfType<ScrollViewer>());
        Assert.Empty(separator!.GetLogicalAncestors().OfType<ScrollViewer>());
    }

    [AvaloniaFact]
    public void A_dialog_opens_at_a_share_of_the_window_that_owns_it()
    {
        var owner = ShortOwner();
        var dialog = OpenSettings(owner);

        Assert.Equal(owner.ClientSize.Height * WindowMetrics.DialogHeightFraction, dialog.Bounds.Height, 0);
    }

    // A bound applied after the window opens rather than before it would leave the dialog above its
    // owner's centre by half the difference.
    [AvaloniaFact]
    public void A_bounded_dialog_is_still_centred_on_the_window_that_owns_it()
    {
        var owner = ShortOwner();
        var dialog = OpenSettings(owner);

        var dialogCentre = dialog.Position.Y + (dialog.Bounds.Height / 2);
        var ownerCentre = owner.Position.Y + (owner.Bounds.Height / 2);
        Assert.True(Math.Abs(dialogCentre - ownerCentre) <= 1,
            $"the dialog's centre is {dialogCentre:F0} and its owner's is {ownerCentre:F0}");
    }

    // A margin on the region rather than on its content leaves the bar short of the band's corners:
    // a gap above it where it should start, and another below.
    [AvaloniaFact]
    public void The_scroll_region_fills_its_band_on_every_side()
    {
        var owner = ShortOwner();
        var dialog = OpenSettings(owner);

        var body = At(Body(dialog), dialog);
        var separatorTop = At(dialog.FindControl<Border>("FooterSeparator")!, dialog).Y;

        Assert.Equal(0, body.X, 0);
        Assert.Equal(0, body.Y, 0);
        Assert.Equal(dialog.Bounds.Width, body.Right, 0);
        Assert.Equal(separatorTop, body.Bottom, 0);
    }

    // The original fault: the bar drew in the same band as the right-hand end of a control. It may
    // take part of the content's inset, but it may not reach the content.
    [AvaloniaFact]
    public void The_scroll_bar_takes_the_content_inset_and_never_the_content()
    {
        var owner = ShortOwner();
        var dialog = OpenSettings(owner);
        var body = Body(dialog);

        Assert.True(body.Extent.Height > body.Viewport.Height, "the body must overflow");
        var bar = body.GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.Orientation == Orientation.Vertical && b.Bounds.Width > 0);
        var barLeft = At(bar, dialog).X;
        Assert.Equal(dialog.Bounds.Width, At(bar, dialog).Right, 0);

        var covered = new List<string>();
        foreach (var control in body.GetVisualDescendants().OfType<Control>())
        {
            if (control is not (Button or ComboBox or CheckBox or TextBox or ListBox or RadioButton)) continue;
            if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0) continue;
            if (control.FindAncestorOfType<ScrollBar>() is not null) continue;
            var right = At(control, dialog).Right;
            if (right > barLeft)
                covered.Add($"{control.GetType().Name} reaches {right:F0} past the bar at {barLeft:F0}");
        }

        Assert.True(covered.Count == 0, string.Join("; ", covered));
    }

    // The editor spans both rows of the style surface, so an Auto second row lets the grid pad the
    // header's row to share the editor's height — which pushes "Text styles" down, leaves a band of
    // nothing under it, and pushes everything below the surface down again. The star row puts the
    // header at the top and closes the list level with the editor's last line.
    [AvaloniaFact]
    public void The_style_list_starts_under_its_header_and_ends_level_with_the_editor()
    {
        var owner = ShortOwner();
        var dialog = OpenSettings(owner);

        var list = dialog.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "TextStylesList");
        var surface = list.GetVisualAncestors().OfType<Grid>()
            .First(g => g.ColumnDefinitions.Count == 2 && g.RowDefinitions.Count == 2);
        var header = surface.Children.OfType<Control>()
            .Single(c => Grid.GetRow(c) == 0 && Grid.GetColumn(c) == 0);
        var editor = (Panel)surface.Children.OfType<Control>().Single(c => Grid.GetRowSpan(c) == 2);
        var lastLine = editor.Children.OfType<Control>().Last();

        double Bottom(Visual v) => v.TranslatePoint(new Point(0, v.Bounds.Height), surface)!.Value.Y;

        Assert.Equal(0, header.TranslatePoint(new Point(0, 0), surface)!.Value.Y, 0);
        Assert.Equal(header.DesiredSize.Height, header.Bounds.Height, 0);
        Assert.Equal(Bottom(lastLine), Bottom(list), 0);
    }

    // SizeToContent would snap the window back to its content height, so the shell releases it once
    // the window is up. The minimum is then the whole safety net: a dialog dragged to nothing would
    // take its own Save and Cancel with it.
    [AvaloniaFact]
    public void A_resizable_dialog_grows_but_never_loses_its_footer()
    {
        var owner = ShortOwner();
        var dialog = OpenSettings(owner);
        var opened = dialog.Bounds.Height;

        dialog.Height = opened + 300;
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SizeToContent.Manual, dialog.SizeToContent);
        Assert.True(dialog.Bounds.Height >= opened + 299, "the dialog would not grow");

        dialog.Height = 1;
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var footer = dialog.FindControl<StackPanel>("ButtonPanel")!;
        Assert.True(At(footer, dialog).Bottom <= dialog.Bounds.Height + 0.5, "the footer left the window");
        Assert.True(Body(dialog).Bounds.Height >= WindowMetrics.DialogBodyMinHeight - 0.5,
            "the body was squeezed below the room a field and its label need");
    }

    // The startup-failure notice is the application's only window: it has no owner to be bounded by,
    // and no way to be resized back if it opens taller than the display.
    [AvaloniaFact]
    public void The_startup_failure_notice_is_bounded_by_the_screen()
    {
        var notice = MessageDialog.CreateStartupFailure("DayNote could not start", "Something went wrong.");
        _open.Add(notice);

        var screen = notice.Screens.Primary!;
        Assert.Equal(
            WindowMetrics.DialogMaxHeight(0, screen.WorkingArea.Height, screen.Scaling),
            notice.MaxHeight);
        Assert.True(double.IsFinite(notice.MaxHeight), "the notice is bounded by nothing");
    }

    // And the startup path has to reach it through that factory. The bound lives there rather than in
    // the constructor, so a lifetime that builds the dialog itself gets an unbounded window and the
    // test above still passes — it would be calling the factory nobody used.
    [Fact]
    public void The_startup_path_builds_its_window_through_the_bounded_factory()
    {
        var app = File.ReadAllText(Path.Combine(SourceDirectory(), "App.axaml.cs"));

        Assert.DoesNotContain("new MessageDialog(", app, StringComparison.Ordinal);
        Assert.Contains("MessageDialog.CreateStartupFailure(", app, StringComparison.Ordinal);
    }

    private static string SourceDirectory([CallerFilePath] string callerPath = "")
    {
        // This file: <repo>/tests/DayNote.Tests/Views/DialogBaseLayoutTests.cs
        var testsViewsDir = Path.GetDirectoryName(callerPath)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testsViewsDir, "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "DayNote");
    }

    public void Dispose()
    {
        for (var i = _open.Count - 1; i >= 0; i--)
            _open[i].Close();
        _open.Clear();
        Dispatcher.UIThread.RunJobs();
    }
}
