using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DayNote.Logging;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class AboutDialogTests
{
    [AvaloniaFact]
    public void Link_failure_stays_inline_and_hides_diagnostics()
    {
        var hostile = new IOException("EACCES IPC /private/tmp/DAYNOTE-LINK-SENTINEL");
        var dialog = new AboutDialog(new NullLogger(), _ => Task.FromException(hostile));
        dialog.Show();

        var link = dialog.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "GitHubLinkButton");
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var result = dialog.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "AboutLinkResult");
        Assert.True(result.IsVisible);
        Assert.Contains("could not be opened", AutomationProperties.GetName(result), StringComparison.Ordinal);
        Assert.DoesNotContain("EACCES", AutomationProperties.GetName(result), StringComparison.Ordinal);
        Assert.DoesNotContain("DAYNOTE-LINK-SENTINEL", AutomationProperties.GetName(result), StringComparison.Ordinal);

        dialog.Close();
    }

    // About opens with the app's own name at the product-title size, and the version on its own line
    // under it rather than sharing the name's line (modal-dialog conventions; the fleet About pattern).
    [AvaloniaFact]
    public void The_name_leads_with_the_version_under_it()
    {
        var dialog = new AboutDialog(new NullLogger());
        dialog.Show();
        try
        {
            var body = dialog.GetVisualDescendants().OfType<ContentPresenter>().Single(presenter => presenter.Name == "DialogContent");
            var blocks = body.GetVisualDescendants().OfType<TextBlock>().ToList();
            var name = blocks.Single(block => block.Name == "AboutAppName");
            var version = blocks.Single(block => block.Name == "AboutAppVersion");

            Assert.Equal("DayNote", name.Text);
            Assert.Same(blocks.First(block => !string.IsNullOrEmpty(block.Text)), name);
            Assert.True(name.FontSize >= 20 && name.FontSize > version.FontSize);
            Assert.True(version.TranslatePoint(default, name)!.Value.Y >= name.Bounds.Height);
        }
        finally
        {
            dialog.Close();
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Debug(string message, object? data = null, Exception? error = null) { }
        public void Info(string message, object? data = null, Exception? error = null) { }
        public void Warn(string message, object? data = null, Exception? error = null) { }
        public void Error(string message, object? data = null, Exception? error = null) { }
    }
}
