using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Core.Configuration;
using DayNote.I18n;
using DayNote.Services;
using DayNote.Tests.Storage;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.I18n;

/// <summary>
/// A label fits its control in every language (localization conventions).
///
/// Buttons and menus size to their words, so a longer language makes them wider rather than cutting
/// them off. What does not grow is anything given a fixed width: the dialogs, the side panes at their
/// default widths, and the status bar. So these open each of those in all ten
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

        AssertPaneHeadersKeepTheirButtons(window, tag);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Tags))]
    public async Task metadata_binder_paths_and_attachment_details_remain_visible_when_narrow(string tag)
    {
        using var speaking = Localizer.Speaking(tag);
        using var populated = PopulatedMainWindow.Create();
        var window = Show(populated.Window);
        await populated.FillAsync();
        var normalWidth = window.Width;
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        foreach (var width in new[] { window.MinWidth, normalWidth })
        {
            window.RequestedThemeVariant = theme;
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            AssertNothingClipped(window, window, tag, atLeast: 20);
            AssertPaneHeadersKeepTheirButtons(window, tag);
            var metadata = window.FindControl<BaselineWrapPanel>("StatusLine")!;
            var details = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Classes.Contains("attachmentDetails")).ToArray();
            Assert.True(details.Length >= 2);
            // A file name takes two lines at most and then trims, inside its row.
            var fileNames = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == "AttachmentFileName").ToArray();
            Assert.True(fileNames.Length >= 2);
            var attachList = window.FindControl<ListBox>("AttachList")!;
            foreach (var name in fileNames)
            {
                Assert.True(name.TextLayout.TextLines.Count <= 2, $"{tag}: {name.Text} takes {name.TextLayout.TextLines.Count} lines");
                var origin = name.TranslatePoint(default, attachList)!.Value;
                Assert.True(origin.X + name.Bounds.Width <= attachList.Bounds.Width + Tolerance);
            }
            var paths = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == "BinderPath").ToArray();
            Assert.NotEmpty(paths);
            // A binder path takes two lines at most and then trims, inside its row.
            var bindersList = window.FindControl<ListBox>("BindersList")!;
            foreach (var path in paths)
            {
                Assert.True(path.TextLayout.TextLines.Count <= 2, $"{tag}: {path.Text} takes {path.TextLayout.TextLines.Count} lines");
                var origin = path.TranslatePoint(default, bindersList)!.Value;
                Assert.True(origin.X + path.Bounds.Width <= bindersList.Bounds.Width + Tolerance);
            }
            // The text style names a font the user typed, so it alone trims, at one line and its width.
            var style = window.FindControl<TextBlock>("TextStyleStatus")!;
            Assert.Equal(TextTrimming.CharacterEllipsis, style.TextTrimming);
            Assert.True(style.Bounds.Width <= style.MaxWidth + Tolerance);
            foreach (var text in metadata.GetVisualDescendants().OfType<TextBlock>().Concat(details)
                         .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text) && text != style))
            {
                Assert.Equal(TextTrimming.None, text.TextTrimming);
                var surface = details.Contains(text) ? (Visual)window.FindControl<ListBox>("AttachList")! : metadata;
                var start = text.TranslatePoint(default, surface)!.Value;
                Assert.True(start.X >= -Tolerance && start.Y >= -Tolerance);
                Assert.True(start.X + text.Bounds.Width <= surface.Bounds.Width + Tolerance);
                Assert.True(start.Y + text.Bounds.Height <= surface.Bounds.Height + Tolerance);
                Assert.True(text.TextLayout.Height <= text.Bounds.Height + Tolerance);
                Assert.True(text.TextLayout.Width <= text.Bounds.Width + Tolerance, $"{tag}: {text.Text} needs {text.TextLayout.Width} in {text.Bounds.Width}");
            }
            // Metadata that cannot share one line takes more lines instead of being cut.
            var shown = metadata.Children.Where(child => child.IsVisible).ToArray();
            if (shown.Sum(child => child.DesiredSize.Width) > metadata.Bounds.Width + Tolerance)
                Assert.True(shown.Select(child => child.Bounds.Y).Distinct().Count() > 1, $"{tag}: the metadata did not wrap.");
            AssertStatusTextSharesBaselines(metadata, tag);
            // Optional rendered evidence uses the same real layout as the regression.
            if (System.Environment.GetEnvironmentVariable("DAYNOTE_LAYOUT_EVIDENCE") is { Length: > 0 } directory)
            {
                System.IO.Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(System.IO.Path.Combine(directory, $"{tag}-{theme}-{width:0}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
    }

    public static TheoryData<string> LongestTags() => new() { "en", "de", "ru", "ja" };

    // Long user values on every surface that shows one, at both widths: identifying text stays within
    // its row at one or two lines, messages stay inside their height limit, and every pane keeps its
    // controls (interface-styling-conventions, Labels).
    [AvaloniaTheory]
    [MemberData(nameof(LongestTags))]
    public async Task long_values_keep_every_pane_in_shape(string tag)
    {
        using var speaking = Localizer.Speaking(tag);
        using var populated = PopulatedMainWindow.Create();
        var window = Show(populated.Window);
        await populated.FillLongAsync();
        var normalWidth = window.Width;
        foreach (var width in new[] { window.MinWidth, normalWidth })
        {
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            AssertNothingClipped(window, window, tag, atLeast: 10);
            AssertPaneHeadersKeepTheirButtons(window, tag);

            foreach (var name in new[] { "BinderTitle", "NoteTitle" })
            foreach (var text in window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == name && text.IsEffectivelyVisible))
                Assert.Single(text.TextLayout.TextLines);
            foreach (var name in new[] { "BinderPath", "AttachmentFileName" })
            foreach (var text in window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == name && text.IsEffectivelyVisible))
                Assert.True(text.TextLayout.TextLines.Count <= 2, $"{tag}: {text.Text}");

            // Every row's controls stay inside its list, and the remove buttons stay reachable.
            foreach (var list in window.GetVisualDescendants().OfType<ListBox>())
            foreach (var control in list.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible && control is TextBlock or Button))
            {
                var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), list)!.Value.X;
                Assert.True(right <= list.Bounds.Width + Tolerance, $"{tag}: {control.GetType().Name} {(control as TextBlock)?.Text} ends at {right:0} in {list.Bounds.Width:0}");
            }

            // The title field keeps room to type in; its status and lock move below it before squeezing it.
            var title = window.FindControl<TextBox>("TitleBox")!;
            Assert.True(title.Bounds.Width >= title.MinWidth - Tolerance, $"{tag}: the title field is {title.Bounds.Width:0} px wide.");
            var lockToggle = window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("LockedToggle")!;
            var editor = window.FindControl<Border>("EditorPane")!;
            Assert.True(lockToggle.TranslatePoint(new Point(lockToggle.Bounds.Width, 0), editor)!.Value.X <= editor.Bounds.Width + Tolerance);

            var result = window.FindControl<TextBlock>("AttachmentResultText")!;
            Assert.True(result.IsEffectivelyVisible);
            var viewport = result.FindAncestorOfType<ScrollViewer>()!;
            Assert.True(viewport.Bounds.Height <= viewport.MaxHeight + Tolerance);

            if (System.Environment.GetEnvironmentVariable("DAYNOTE_LAYOUT_EVIDENCE") is { Length: > 0 } directory)
            {
                System.IO.Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(System.IO.Path.Combine(directory, $"long-{tag}-{width:0}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
    }

    // The questions that quote a user's value keep it to one shortened mention and keep their actions.
    [AvaloniaTheory]
    [MemberData(nameof(LongestTags))]
    public async Task questions_quoting_long_values_keep_their_actions(string tag)
    {
        using var speaking = Localizer.Speaking(tag);
        var owner = Show(new Window());
        var service = new DialogService(new NullLogger()) { Owner = owner };
        var longName = string.Concat(Enumerable.Repeat("Ein-sehr-langer-Bindername-", 12));

        async Task Check<T>(Task<T> asking, string answerTag, string file)
        {
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(owner.OwnedWindows);
            AssertNothingClipped(dialog, dialog, tag, atLeast: 2);
            var message = dialog.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text?.Contains("Ein-sehr") == true);
            Assert.DoesNotContain(longName, message.Text!, System.StringComparison.Ordinal);
            if (System.Environment.GetEnvironmentVariable("DAYNOTE_LAYOUT_EVIDENCE") is { Length: > 0 } directory)
            {
                System.IO.Directory.CreateDirectory(directory);
                using var frame = dialog.CaptureRenderedFrame();
                frame!.Save(System.IO.Path.Combine(directory, $"{file}-{tag}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }

            dialog.GetVisualDescendants().OfType<Button>().First(button => Equals(button.Tag, answerTag))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await asking;
        }

        await Check(service.AskExternalChangeAsync(QuotedValue.Name(longName)), "keep", "outside-change");
        await Check(service.AskQuitWithUnsavedBinderAsync(QuotedValue.Name(longName)), "cancel", "quit");
        await Check(service.ConfirmAsync(Message.Of("note.deleteTitle"), Message.Of("note.deleteNamed", ("title", QuotedValue.Name(longName))), "common.delete", destructive: true), "cancel", "delete");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Tags))]
    public async Task the_save_and_attachment_questions_keep_their_actions_visible(string tag)
    {
        using var speaking = Localizer.Speaking(tag);
        var owner = Show(new Window());
        var service = new DialogService(new NullLogger()) { Owner = owner };
        var outside = service.AskExternalChangeAsync("Journal");
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(owner.OwnedWindows);
        AssertNothingClipped(dialog, dialog, tag, atLeast: 2);
        dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "keep"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await outside;

        var quit = service.AskQuitWithUnsavedBinderAsync("Journal");
        Dispatcher.UIThread.RunJobs();
        dialog = Assert.Single(owner.OwnedWindows);
        AssertNothingClipped(dialog, dialog, tag, atLeast: 3);
        dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "cancel"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await quit;

        var settled = new TaskCompletionSource();
        var wait = Show(new AttachmentWaitDialog(settled.Task, System.TimeSpan.FromHours(1)));
        try
        {
            // Timer behavior is covered separately; measure the state after Stop is offered.
            wait.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, AttachmentWaitDialog.StopTag)).IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            AssertNothingClipped(wait, wait, tag, atLeast: 1);
        }
        finally
        {
            settled.SetResult();
            wait.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, AttachmentWaitDialog.StopTag))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Tags))]
    public void the_records_window_clips_nothing_at_its_minimum_size(string tag)
    {
        using var speaking = Localizer.Speaking(tag);

        var source = new DayNote.Tests.ViewModels.FakeRecordsSource();
        source.Add(3, level: "warn");
        var vm = new DayNote.ViewModels.RecordsWindowViewModel(source, new DayNote.Tests.ViewModels.FakeRecordsHost(), new NullLogger());
        var window = Show(new RecordsWindow(vm));
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        vm.SelectedRecord = vm.Records[0];
        Dispatcher.UIThread.RunJobs();

        // The rows' levels, the detail's level and its Details heading cannot move.
        AssertNothingClipped(window, window, tag, atLeast: 4);
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

    // Each side pane's header keeps its heading whole and its buttons inside the pane: when both do
    // not fit on one line, the buttons move below the heading instead of cutting it short.
    // Texts on one line of the status bar sit on one baseline, whatever fonts their scripts draw with:
    // Latin digits beside Japanese dates once stood a pixel higher.
    private static void AssertStatusTextSharesBaselines(BaselineWrapPanel line, string tag)
    {
        var texts = line.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
            .Select(text => (Text: text.Text, Top: text.TranslatePoint(default, line)!.Value.Y,
                Baseline: text.TranslatePoint(default, line)!.Value.Y + text.Padding.Top + text.TextLayout.TextLines[0].Baseline))
            .ToArray();
        Assert.True(texts.Length >= 5);
        // Lines stand at least a text height apart, while the texts of one line differ by a pixel or two.
        var rows = new List<List<(string? Text, double Top, double Baseline)>>();
        foreach (var text in texts.OrderBy(text => text.Top))
        {
            if (rows.Count == 0 || text.Top - rows[^1][0].Top > 8) rows.Add([]);
            rows[^1].Add(text);
        }

        foreach (var row in rows)
        {
            var baselines = row.Select(text => text.Baseline).ToArray();
            Assert.True(baselines.Max() - baselines.Min() < 0.5,
                $"{tag}: {string.Join(", ", row.Select(text => $"{text.Text} at {text.Baseline:0.00}"))}");
        }
    }

    private static void AssertPaneHeadersKeepTheirButtons(Window window, string tag)
    {
        foreach (var header in window.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("paneHeader")))
        {
            foreach (var button in header.GetVisualDescendants().OfType<Button>())
            {
                var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), header)!.Value.X;
                Assert.True(
                    right <= header.Bounds.Width + Tolerance,
                    $"{tag}: “{button.Content}” ends at {right:0} px in a {header.Bounds.Width:0} px pane header.");
            }
        }
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
