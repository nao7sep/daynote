using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using DayNote.Core.Configuration;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class SettingsDialogTests
{
    [AvaloniaFact]
    public void FailedSaveKeepsDraftOpenAndShowsInlineError()
    {
        var attempts = 0;
        var dialog = new SettingsDialog(new AppConfig(), _ =>
        {
            attempts++;
            return false;
        });
        var font = dialog.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(box => box.PlaceholderText == AppConfig.DefaultUiFontFamily);
        font.Text = "Menlo";
        var save = dialog.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => Equals(button.Tag, "ok"));

        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, attempts);
        Assert.False(dialog.Applied);
        Assert.Equal("Menlo", font.Text);
        var error = dialog.GetLogicalDescendants().OfType<TextBlock>().Single(block =>
            block.IsVisible && block.Text?.Contains("could not be saved") == true);
        Assert.True(error.Foreground?.Opacity > 0);
    }

    [AvaloniaFact]
    public void SuccessfulSaveCommitsExactlyOnce()
    {
        var attempts = 0;
        var dialog = new SettingsDialog(new AppConfig(), _ =>
        {
            attempts++;
            return true;
        });
        var font = dialog.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(box => box.PlaceholderText == AppConfig.DefaultUiFontFamily);
        font.Text = "Menlo";
        var save = dialog.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => Equals(button.Tag, "ok"));

        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, attempts);
        Assert.True(dialog.Applied);
    }
}
