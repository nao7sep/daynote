using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    public void The_time_zone_is_chosen_from_a_list_that_starts_with_system()
    {
        var config = new AppConfig();
        var dialog = new SettingsDialog(config, _ => true);
        var zones = Named<ComboBox>(dialog, "TimeZoneBox");
        var options = zones.Items.OfType<TimeZoneOption>().ToList();
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));

        Assert.Equal("system", options[0].Value);
        Assert.Contains(DayNote.Core.Time.DayNoteTime.SystemZoneId(), options[0].Name);
        Assert.Same(options[0], zones.SelectedItem);
        Assert.Contains(options, option => option.Value == "Asia/Tokyo");
        Assert.DoesNotContain(dialog.GetLogicalDescendants().OfType<TextBox>(), box => box.Text == "system");
        Assert.False(save.IsEnabled);

        zones.SelectedItem = options.Single(option => option.Value == "Europe/Berlin");

        Assert.Equal("Europe/Berlin", config.TimeZone);
        Assert.True(save.IsEnabled);
    }

    [AvaloniaFact]
    public void A_saved_zone_no_platform_knows_shows_as_system_without_holding_save_disabled()
    {
        var config = new AppConfig { TimeZone = "Mars/Phobos" };
        var dialog = new SettingsDialog(config, _ => true);
        var zones = Named<ComboBox>(dialog, "TimeZoneBox");
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));

        Assert.Equal("system", ((TimeZoneOption)zones.SelectedItem!).Value);
        dialog.GetLogicalDescendants().OfType<TextBox>()
            .Single(box => box.PlaceholderText == AppConfig.DefaultUiFontFamily).Text = "Menlo";
        Dispatcher.UIThread.RunJobs();
        Assert.True(save.IsEnabled);
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

    [AvaloniaFact]
    public void A_styles_row_says_what_was_typed_even_when_no_such_font_is_installed()
    {
        var config = new AppConfig();
        var dialog = new SettingsDialog(config, _ => true, _ => Task.FromResult(true));
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var list = Named<ListBox>(dialog, "TextStylesList");
        Named<Button>(dialog, "AddTextStyleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var family = Named<TextBox>(dialog, "TextStyleFontFamily");

        // \u904a (U+904A) is the IME's usual answer for \u3086\u3046; no font declares this name.
        family.Text = "\u904a\u660e\u671d\u4f53";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("\u904a\u660e\u671d\u4f53", RowLabel(list, 2));

        // A name given as a list goes by its first family, so the built-in default reads as Menlo.
        family.Text = EditorTextStyle.DefaultFixedWidthFamilies;
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Menlo", RowLabel(list, 2), StringComparison.Ordinal);
        dialog.Close();
    }

    [AvaloniaFact]
    public void Removing_a_style_asks_first_and_a_refusal_keeps_it()
    {
        var config = new AppConfig();
        var asked = new List<string>();
        var answer = false;
        var dialog = new SettingsDialog(config, _ => true, label =>
        {
            asked.Add(label);
            return Task.FromResult(answer);
        });
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var list = Named<ListBox>(dialog, "TextStylesList");
        list.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var remove = Named<Button>(dialog, "RemoveTextStyleButton");
        var removed = config.TextStyles[1].FontFamily;

        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // The trigger of a destructive path asks, and a no leaves the list alone.
        Assert.Equal([removed], asked);
        Assert.Equal(2, config.TextStyles.Count);
        Assert.Contains("danger", remove.Classes);

        answer = true;
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, asked.Count);
        Assert.DoesNotContain(config.TextStyles, style => style.FontFamily == removed);
        dialog.Close();
    }

    [AvaloniaFact]
    public void A_number_steps_with_a_minus_and_a_plus_in_that_order()
    {
        var dialog = new SettingsDialog(new AppConfig(), _ => true);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        dialog.UpdateLayout();
        var size = dialog.GetVisualDescendants().OfType<NumericUpDown>().First();

        var buttons = size.GetVisualDescendants().OfType<RepeatButton>()
            .OrderBy(button => button.TranslatePoint(default, size)!.Value.X)
            .ToList();
        var glyphs = buttons
            .Select(button => button.GetVisualDescendants().OfType<PathIcon>().Single().Data!.Bounds)
            .ToList();

        Assert.Equal(["PART_DecreaseButton", "PART_IncreaseButton"], buttons.Select(button => button.Name));
        // A bar as wide as the square cross beside it: minus, then plus, and no chevron either side.
        Assert.Equal(glyphs[0].Width, glyphs[1].Width);
        Assert.Equal(glyphs[1].Width, glyphs[1].Height);
        Assert.True(glyphs[0].Height < glyphs[1].Height / 4, $"The minus is {glyphs[0].Height:0.##} tall.");
        dialog.Close();
    }

    private static string RowLabel(ListBox list, int index) =>
        list.ContainerFromIndex(index)!.GetLogicalDescendants().OfType<TextBlock>().First().Text!;

    private static bool[] DefaultBadges(ListBox list) =>
        Enumerable.Range(0, list.ItemCount)
            .Select(index => list.ContainerFromIndex(index)!.GetLogicalDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("badge")).IsVisible)
            .ToArray();

    private static T Named<T>(SettingsDialog dialog, string name) where T : Control =>
        dialog.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
}
