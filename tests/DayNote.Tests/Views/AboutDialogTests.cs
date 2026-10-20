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
using DayNote.Tests.I18n;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class AboutDialogTests
{
    [AvaloniaFact]
    public void Link_failure_stays_inline_and_hides_diagnostics()
    {
        var hostile = new IOException("EACCES IPC /private/tmp/DAYNOTE-LINK-SENTINEL");
        var dialog = new AboutDialog(new NullLogger(), _ => Task.FromException<bool>(hostile));
        dialog.Show();

        var link = dialog.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "GitHubLinkButton");
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var result = dialog.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "AboutLinkResult");
        Assert.True(result.IsVisible);
        Assert.Equal(English.Of("failure.openExternalLink"), AutomationProperties.GetName(result));
        Assert.DoesNotContain("EACCES", AutomationProperties.GetName(result), StringComparison.Ordinal);
        Assert.DoesNotContain("DAYNOTE-LINK-SENTINEL", AutomationProperties.GetName(result), StringComparison.Ordinal);

        dialog.Close();
    }

    [AvaloniaFact]
    public void A_declined_launch_stays_inline_until_a_confirmed_retry()
    {
        var success = false;
        var dialog = new AboutDialog(new NullLogger(), _ => Task.FromResult(success));
        dialog.Show();
        try
        {
            var link = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "GitHubLinkButton");
            var result = dialog.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "AboutLinkResult");
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(result.IsVisible);
            success = true;
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(result.IsVisible);
        }
        finally { dialog.Close(); }
    }

    // About opens with the app's own name at the product-title size, and the version on its own line
    // under it rather than sharing the name's line, so the name reads first and the version stays
    // findable without competing with it.
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
