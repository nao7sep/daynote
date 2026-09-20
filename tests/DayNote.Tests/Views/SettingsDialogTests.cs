using Avalonia.Headless;
using Avalonia.Input;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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

    [AvaloniaFact]
    public void Add_appends_a_built_in_style_then_selects_reveals_and_focuses_its_family()
    {
        var config = new AppConfig();
        config.TextStyles[0].FontSize = 31;
        config.TextStyles[0].Bold = true;
        var dialog = new SettingsDialog(config, _ => true);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var list = Named<ListBox>(dialog, "TextStylesList");
        var family = Named<TextBox>(dialog, "TextStyleFontFamily");

        Named<Button>(dialog, "AddTextStyleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var added = config.TextStyles[^1];
        Assert.Equal(3, config.TextStyles.Count);
        Assert.Equal(new EditorTextStyle().FontFamily, added.FontFamily);
        Assert.Equal(new EditorTextStyle().FontSize, added.FontSize);
        Assert.False(added.Bold);
        Assert.False(added.IsDefault);
        Assert.Equal(2, list.SelectedIndex);
        Assert.NotNull(list.ContainerFromIndex(2));
        Assert.True(family.IsFocused);
        Assert.Equal(added.FontFamily, family.Text);
        dialog.Close();
    }

    [AvaloniaFact]
    public void The_default_shows_in_the_list_and_set_as_default_moves_it()
    {
        var config = new AppConfig();
        var dialog = new SettingsDialog(config, _ => true);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var list = Named<ListBox>(dialog, "TextStylesList");
        var setDefault = Named<Button>(dialog, "SetDefaultTextStyleButton");
        var remove = Named<Button>(dialog, "RemoveTextStyleButton");

        Assert.Equal(new[] { true, false }, DefaultBadges(list));
        Assert.False(setDefault.IsEnabled);
        Assert.False(remove.IsEnabled);

        list.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.True(setDefault.IsEnabled);
        setDefault.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { false, true }, config.TextStyles.Select(style => style.IsDefault));
        Assert.Equal(new[] { false, true }, DefaultBadges(list));
        Assert.False(remove.IsEnabled);
        dialog.Close();
    }

    [AvaloniaFact]
    public void Keyboard_reorder_moves_the_durable_order_and_keeps_the_selection()
    {
        var config = new AppConfig();
        var families = config.TextStyles.Select(style => style.FontFamily).ToArray();
        var dialog = new SettingsDialog(config, _ => true);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var list = Named<ListBox>(dialog, "TextStylesList");
        Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0)).Focus();
        var command = ShortcutCatalog.CommandModifier(dialog) == KeyModifiers.Meta
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;

        dialog.KeyPress(Key.Down, command | RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(families.Reverse(), config.TextStyles.Select(style => style.FontFamily));
        Assert.Equal(1, list.SelectedIndex);
        Assert.True(list.IsKeyboardFocusWithin);
        dialog.Close();
    }

    [AvaloniaFact]
    public void A_cleared_number_keeps_the_presets_value_and_shows_it_again()
    {
        var config = new AppConfig();
        var dialog = new SettingsDialog(config, _ => true);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var size = dialog.GetLogicalDescendants().OfType<NumericUpDown>().First();
        var before = config.TextStyles[0].FontSize;

        size.Focus();
        size.Value = null;
        Named<TextBox>(dialog, "TextStyleFontFamily").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, config.TextStyles[0].FontSize);
        Assert.Equal((decimal)before, size.Value);
        dialog.Close();
    }

    private static bool[] DefaultBadges(ListBox list) =>
        Enumerable.Range(0, list.ItemCount)
            .Select(index => list.ContainerFromIndex(index)!.GetLogicalDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("badge")).IsVisible)
            .ToArray();

    private static T Named<T>(SettingsDialog dialog, string name) where T : Control =>
        dialog.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
}
