using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
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

    private sealed class NullLogger : IAppLogger
    {
        public void Debug(string message, object? data = null, Exception? error = null) { }
        public void Info(string message, object? data = null, Exception? error = null) { }
        public void Warn(string message, object? data = null, Exception? error = null) { }
        public void Error(string message, object? data = null, Exception? error = null) { }
    }
}
