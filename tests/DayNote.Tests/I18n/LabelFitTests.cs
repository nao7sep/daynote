using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Core.Configuration;
using DayNote.I18n;
using DayNote.Tests.Storage;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.I18n;

/// <summary>
/// A label fits its control in every language (localization conventions).
///
/// Buttons and menus size to their words, so a longer language makes them wider rather than cutting
/// them off. What does not grow is anything given a fixed width: the dialogs, the side panes at their
/// default widths, and the status bar, which never wraps. So these open each of those in all ten
/// languages and measure the text, with the real font and the real layout rather than by eye: against
/// the room each label was given, and against the edge of the surface it sits on, which catches a row
/// that pushes its last words past the end rather than squeezing any one label.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public class LabelFitTests : WindowTest
{
    // A label may exceed its box by this much before it is called clipped: Skia's measurement and
    // Avalonia's arrangement round differently, and a fraction of a pixel is not a defect.
    private const double Tolerance = 1.0;

    public static TheoryData<string> Tags() => new() { "en", "de", "es", "fr", "it", "pt-BR", "ru", "ja", "ko", "zh-Hans" };

    [AvaloniaTheory]
    [MemberData(nameof(Tags))]
    public void the_settings_dialog_clips_nothing(string tag)
    {
        using var speaking = Localizer.Speaking(tag);

        var dialog = Show(new SettingsDialog(new AppConfig(), _ => true));

        // The theme choices, the checkboxes, the style list and its badge, and the buttons.
        AssertNothingClipped(dialog, Content(dialog), tag, atLeast: 10);

        // English keeps the checkboxes and the style's actions on one line, as the dialog was drawn;
        // only a language whose labels do not fit moves the actions below.
        if (tag == "en")
        {
            var row = dialog.GetVisualDescendants().OfType<LeadingTrailingRow>().Single();
            Assert.True(row.Children[1].Bounds.Y < row.Children[0].Bounds.Bottom, "the style's actions left the checkboxes' line");
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Tags))]
    public void the_shortcuts_dialog_clips_nothing(string tag)
    {
        using var speaking = Localizer.Speaking(tag);

        var owner = Show(new Window());
        var dialog = Show(new ShortcutsDialog(ShortcutCatalog.Build(owner)));

        // Key legends and the Close button; the headers and descriptions wrap by design.
        AssertNothingClipped(dialog, Content(dialog), tag, atLeast: 10);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Tags))]
    public void the_about_dialog_clips_nothing(string tag)
    {
        using var speaking = Localizer.Speaking(tag);

        var dialog = Show(new AboutDialog(new NullLogger()));

        // The body wraps by design; the name, the licence line and the Close button cannot move.
        AssertNothingClipped(dialog, Content(dialog), tag, atLeast: 3);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Tags))]
    public async Task the_main_window_clips_nothing_at_its_default_size(string tag)
    {
        using var speaking = Localizer.Speaking(tag);

        using var populated = PopulatedMainWindow.Create();
        var window = Show(populated.Window);
        await populated.FillAsync();
        Dispatcher.UIThread.RunJobs();

        // Pane headers and their buttons, the status picker, the badges and the status bar.
        AssertNothingClipped(window, window, tag, atLeast: 20);

        // Each side pane's header keeps its buttons inside the pane at its default width; the title
        // beside them trims rather than pushing them out.
        foreach (var header in window.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("paneHeader")))
        {
            var buttons = header.GetVisualDescendants().OfType<Button>().ToArray();
            foreach (var button in buttons)
            {
                var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), header)!.Value.X;
                Assert.True(
                    right <= header.Bounds.Width + Tolerance,
                    $"{tag}: “{button.Content}” ends at {right:0} px in a {header.Bounds.Width:0} px pane header.");
            }
        }
    }

    // A theory over a whole window, with the checks for what cannot grow: every label's words fit the
    // box it was given, and every box ends inside the surface.
    private static void AssertNothingClipped(Visual root, Visual surface, string tag, int atLeast)
    {
        var clipped = new List<string>();
        var measured = 0;

        foreach (var text in root.GetVisualDescendants().OfType<TextBlock>())
        {
            // Text that wraps or is deliberately trimmed is not clipped; neither is a label that was
            // never given a size, which happens to anything not currently on screen.
            if (text.Text is not { Length: > 0 } words || !text.IsEffectivelyVisible)
                continue;
            if (text.TextWrapping != TextWrapping.NoWrap || text.TextTrimming != TextTrimming.None)
                continue;
            if (text.Bounds.Width <= 0)
                continue;

            measured++;
            var needed = Measure(words, text);
            if (needed > text.Bounds.Width + Tolerance)
                clipped.Add($"“{words}” needs {needed:F0}px in {text.Bounds.Width:F0}px");

            if (text.TranslatePoint(new Point(text.Bounds.Width, 0), surface) is { } end
                && end.X > surface.Bounds.Width + Tolerance)
            {
                clipped.Add($"“{words}” runs to {end.X:F0}px past the {surface.Bounds.Width:F0}px surface");
            }
        }

        // A row that never wraps but was given less room than its labels need lays them out anyway,
        // over its neighbour or past the edge, without squeezing any one label: the row says so.
        foreach (var row in root.GetVisualDescendants().OfType<StackPanel>())
        {
            if (row.Orientation != Orientation.Horizontal || !row.IsEffectivelyVisible || row.Bounds.Width <= 0)
                continue;
            // The children's own sizes: a row's desired size is clamped to the room it was offered.
            var children = row.Children.Where(child => child.IsVisible).ToArray();
            var needed = children.Sum(child => child.DesiredSize.Width) + row.Spacing * System.Math.Max(0, children.Length - 1);
            if (needed > row.Bounds.Width + Tolerance)
            {
                var words = string.Join(" | ", row.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
                clipped.Add($"a row needs {needed:F0}px in {row.Bounds.Width:F0}px: {words}");
            }
        }

        // A gate that measures nothing would pass forever: if a redesign makes every label wrap or
        // trim, this says so rather than quietly stopping work.
        Assert.True(
            measured >= atLeast,
            $"{tag}: only {measured} labels were measured, fewer than the {atLeast} expected; the check is not looking at anything.");
        Assert.True(clipped.Count == 0, $"{tag}: {string.Join("; ", clipped)}");
    }

    // The dialog's content band: the surface its controls are laid out in, inside the dialog's padding.
    private static Visual Content(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(presenter => presenter.Name == "DialogContent");

    private static double Measure(string words, TextBlock text) =>
        new FormattedText(
            words,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight),
            text.FontSize,
            null).Width;

    private sealed class NullLogger : DayNote.Logging.IAppLogger
    {
        public void Debug(string message, object? data = null, System.Exception? error = null) { }
        public void Info(string message, object? data = null, System.Exception? error = null) { }
        public void Warn(string message, object? data = null, System.Exception? error = null) { }
        public void Error(string message, object? data = null, System.Exception? error = null) { }
    }
}
