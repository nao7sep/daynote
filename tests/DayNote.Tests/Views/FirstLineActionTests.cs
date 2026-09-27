using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Core.Models;
using DayNote.I18n;
using DayNote.Logging;
using DayNote.Tests.I18n;
using DayNote.Tests.Storage;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// A per-item X sits centred on its item's first text line: a row's remove or delete on the title, a
/// result's close on the message's first line however far it wraps. Centred on the whole row, the
/// row's X sat between the title and the line below it; pinned to the top, a result's X sat above
/// its first line. The X moves by a render translation, so it moves nothing else.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class FirstLineActionTests : WindowTest
{
    // Japanese falls back to another font, whose first line is taller than the Latin one.
    [AvaloniaTheory]
    [InlineData("en", "A note", "Could not open the link.",
        "Two of the files could not be added because they are no longer where they were when they " +
        "were dropped here, so nothing was copied for them and the note keeps its other attachments.")]
    [InlineData("ja", "日本語のノート", "リンクを開けませんでした。",
        "ファイルのうち二つは追加できませんでした。ドロップした場所にもうないため、何もコピーされず、" +
        "ノートはほかの添付ファイルをそのまま保持しています。")]
    public async Task Every_row_and_result_X_centres_on_its_first_line(string language, string title, string shortMessage, string longMessage)
    {
        using var populated = PopulatedMainWindow.Create();
        var window = populated.Window;
        Show(window);
        await populated.FillAsync();
        using var speaking = Localizer.Speaking(language);
        // An editable note, so its attachment rows carry their X.
        populated.ViewModel.Editor.Status = NoteStatus.Draft;
        populated.ViewModel.Editor.Title = title;
        Dispatcher.UIThread.RunJobs();
        var messages = new[] { shortMessage, longMessage };

        var rows = window.GetVisualDescendants().OfType<ListBoxItem>().ToList();
        foreach (var click in new[] { "binder", "note", "attachment" })
        {
            var row = rows.First(item => item.GetVisualDescendants().OfType<Button>()
                .Any(button => button.Classes.Contains("rowAction") && button.IsVisible && RowKind(button) == click));
            var action = row.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("rowAction"));
            AssertCentred(FirstText(row), action, window);
        }

        var attachmentResult = window.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("resultFrame") && border.IsVisible && border.FindAncestorOfType<ListBox>() is null);
        var shellResult = window.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("shellResult"));
        foreach (var result in new[] { attachmentResult, shellResult })
            AssertCentredOneLineAndWrapped(result, window, messages);

        var about = Show(new AboutDialog(new QuietLog()));
        var linkResult = about.GetLogicalDescendants().OfType<Border>().Single(border => border.Name == "AboutLinkResult");
        linkResult.IsVisible = true;
        AssertCentredOneLineAndWrapped(linkResult, about, messages);
    }

    private static void AssertCentredOneLineAndWrapped(Border result, Window window, string[] messages)
    {
        var text = result.GetVisualDescendants().OfType<TextBlock>().First(block => block.FindAncestorOfType<Button>() is null);
        var close = result.GetVisualDescendants().OfType<Button>().Single();
        foreach (var message in messages)
        {
            text.Text = message;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AssertCentred(text, close, window);
        }

        Assert.True(text.TextLayout.TextLines.Count >= 3);
    }

    private static void AssertCentred(TextBlock text, Button action, Visual window)
    {
        var line = text.TextLayout.TextLines[0];
        var lineCentre = text.TranslatePoint(new Point(0, text.Padding.Top), window)!.Value.Y + line.Height / 2;
        var actionCentre = action.TranslatePoint(default, window)!.Value.Y + action.Bounds.Height / 2;
        Assert.InRange(actionCentre - lineCentre, -1, 1);
    }

    private static TextBlock FirstText(Visual item) =>
        item.GetVisualDescendants().OfType<TextBlock>()
            .First(text => text.IsVisible && !string.IsNullOrEmpty(text.Text) && text.FindAncestorOfType<Button>() is null);

    private static string RowKind(Button button) =>
        button.FindAncestorOfType<ListBox>()?.Name switch
        {
            "BindersList" => "binder",
            "AttachList" => "attachment",
            _ => "note",
        };

    private sealed class QuietLog : IAppLogger
    {
        public void Debug(string message, object? data = null, Exception? error = null) { }
        public void Info(string message, object? data = null, Exception? error = null) { }
        public void Warn(string message, object? data = null, Exception? error = null) { }
        public void Error(string message, object? data = null, Exception? error = null) { }
    }
}
