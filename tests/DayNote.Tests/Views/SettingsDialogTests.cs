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
using DayNote.Tests.I18n;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class SettingsDialogTests
{
    [AvaloniaFact]
    public void A_blank_font_keeps_its_validation_reason_visible_when_another_style_is_selected()
    {
        var config = new AppConfig();
        var dialog = new SettingsDialog(config, _ => true);
        var family = Named<TextBox>(dialog, "TextStyleFontFamily");
        var list = Named<ListBox>(dialog, "TextStylesList");
        var validation = Named<TextBlock>(dialog, "SettingsValidationError");
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));

        family.Text = "";
        Dispatcher.UIThread.RunJobs();
        var label = TextStyleLabels.For(config.TextStyles, English.Of("settings.noFontFamily"),
            System.Globalization.CultureInfo.GetCultureInfo("en"))[0];
        var expected = English.Of("settings.fontRequired", ("style", label));
        Assert.True(validation.IsVisible);
        Assert.Equal(expected, validation.Text);
        Assert.False(save.IsEnabled);

        list.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.True(validation.IsVisible);
        Assert.Equal(expected, validation.Text);
        Assert.False(save.IsEnabled);

        list.SelectedIndex = 0;
        family.Text = "Menlo";
        Dispatcher.UIThread.RunJobs();
        Assert.False(validation.IsVisible);
        Assert.Equal(string.Empty, validation.Text);
        Assert.True(save.IsEnabled);
    }

    [AvaloniaFact]
    public void Reset_text_styles_fills_the_draft_with_the_built_ins()
    {
        var config = new AppConfig();
        config.TextStyles[0].FontSize = 24;
        AppConfig? saved = null;
        var dialog = new SettingsDialog(config, candidate => { saved = candidate; return true; });
        Named<Button>(dialog, "ResetTextStylesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(15, config.TextStyles[0].FontSize);
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));
        Assert.True(save.IsEnabled);
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(ConfigSets.UserSets(System.Text.Json.JsonSerializer.SerializeToElement(saved!, DayNoteJson.Options)));
    }

    [AvaloniaFact]
    public void Reset_text_styles_with_nothing_to_reset_leaves_save_disabled()
    {
        var dialog = new SettingsDialog(new AppConfig(), _ => true);
        Named<Button>(dialog, "ResetTextStylesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));
        Assert.False(save.IsEnabled);
    }

    [AvaloniaFact]
    public void Font_families_are_cleaned_as_single_lines()
    {
        var config = new AppConfig();
        var dialog = new SettingsDialog(config, _ => true);
        dialog.GetLogicalDescendants().OfType<TextBox>()
            .Single(box => box.PlaceholderText == AppConfig.DefaultUiFontFamily).Text = " Iosevka Term ";
        Named<TextBox>(dialog, "TextStyleFontFamily").Text = "Menlo  ";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Iosevka Term", config.UiFontFamily);
        Assert.Equal("Menlo", config.TextStyles[0].FontFamily);
    }

    [AvaloniaFact]
    public void Escape_cancel_and_the_close_button_ask_before_discarding_changes()
    {
        var config = new AppConfig();
        var asked = 0;
        var answer = false;
        var dialog = new SettingsDialog(config, _ => true, askBeforeDiscarding: () =>
        {
            asked++;
            return Task.FromResult(answer);
        });
        var closed = false;
        dialog.Closed += (_, _) => closed = true;
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Named<Button>(dialog, "AddTextStyleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Cancel(dialog).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        dialog.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, asked);
        Assert.False(closed);

        answer = true;
        Cancel(dialog).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, asked);
        Assert.True(closed);
        Assert.False(dialog.Applied);
    }

    [AvaloniaFact]
    public void A_dialog_without_changes_or_a_save_closes_without_asking()
    {
        var asked = 0;
        Func<Task<bool>> ask = () =>
        {
            asked++;
            return Task.FromResult(false);
        };
        var clean = new SettingsDialog(new AppConfig(), _ => true, askBeforeDiscarding: ask);
        clean.Show();
        Cancel(clean).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var saved = new SettingsDialog(new AppConfig(), _ => true, askBeforeDiscarding: ask);
        saved.Show();
        Dispatcher.UIThread.RunJobs();
        Named<Button>(saved, "AddTextStyleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        saved.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, asked);
        Assert.False(clean.IsVisible);
        Assert.False(saved.IsVisible);
        Assert.True(saved.Applied);
    }

    [Theory]
    [InlineData(WindowCloseReason.WindowClosing, false, true, true)]
    [InlineData(WindowCloseReason.WindowClosing, true, true, false)]
    [InlineData(WindowCloseReason.WindowClosing, false, false, false)]
    [InlineData(WindowCloseReason.OSShutdown, false, true, false)]
    [InlineData(WindowCloseReason.ApplicationShutdown, false, true, false)]
    [InlineData(WindowCloseReason.OwnerWindowClosing, false, true, false)]
    public void Only_a_users_dismiss_of_unsaved_changes_asks(WindowCloseReason reason, bool confirmed, bool unsaved, bool asks) =>
        Assert.Equal(asks, DialogBase.ShouldAskBeforeClosing(reason, confirmed, unsaved));

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
            block.IsVisible && block.Text == English.Of("failure.settingsSave"));
        Assert.True(error.Foreground?.Opacity > 0);
    }

    [AvaloniaFact]
    public void An_empty_stored_ui_font_shows_the_default_as_placeholder_and_stays_empty_on_save()
    {
        var config = new AppConfig();
        Assert.Equal("", config.UiFontFamily);
        var dialog = new SettingsDialog(config, _ => true);
        var font = dialog.GetLogicalDescendants()
            .OfType<TextBox>()
            .Single(box => box.PlaceholderText == AppConfig.DefaultUiFontFamily);

        Assert.Equal("", font.Text);

        // Touch an unrelated field so Save is enabled, then save without typing a UI font.
        var zones = Named<ComboBox>(dialog, "TimeZoneBox");
        var options = zones.Items.OfType<TimeZoneOption>().ToList();
        zones.SelectedItem = options.Single(option => option.Value == "Europe/Berlin");
        Dispatcher.UIThread.RunJobs();
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("", config.UiFontFamily);
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
        var dialog = new SettingsDialog(config, _ => true, askBeforeDiscarding: Discard);
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
        var dialog = new SettingsDialog(config, _ => true, askBeforeDiscarding: Discard);
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
        var dialog = new SettingsDialog(config, _ => true, askBeforeDiscarding: Discard);
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
    public void A_number_the_field_cannot_use_stays_as_typed_with_its_reason_and_holds_save()
    {
        var config = new AppConfig();
        var dialog = new SettingsDialog(config, _ => true);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var size = Named<NumberField>(dialog, "TextStyleFontSize");
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));
        var before = config.TextStyles[0].FontSize;

        foreach (var typed in new[] { "", "14.5", "49", "big" })
        {
            size.Input.Focus();
            size.Input.Text = typed;
            Named<TextBox>(dialog, "TextStyleFontFamily").Focus();
            Dispatcher.UIThread.RunJobs();

            // What was typed stays, a message under the field says what it takes, the preset keeps its
            // last valid size and Save waits.
            Assert.Equal(typed, size.Input.Text);
            Assert.True(size.Error.IsVisible);
            Assert.Equal(English.Of("numberField.wholeRange", ("min", "8"), ("max", "48")), size.Error.Text);
            Assert.Equal(before, config.TextStyles[0].FontSize);
            Assert.False(save.IsEnabled);
        }

        size.Input.Text = "20";
        Dispatcher.UIThread.RunJobs();
        Assert.False(size.Error.IsVisible);
        Assert.Equal(20, config.TextStyles[0].FontSize);
        Assert.True(save.IsEnabled);

        var autosave = Named<NumberField>(dialog, "AutosaveDelay");
        autosave.Input.Text = "0.1";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(English.Of("numberField.range", ("min", "0.25"), ("max", "60")), autosave.Error.Text);
        Assert.Equal(new AppConfig().AutosaveDelaySeconds, config.AutosaveDelaySeconds);
        Assert.False(save.IsEnabled);
        autosave.Input.Text = "0.3";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.3, config.AutosaveDelaySeconds);
        Assert.True(save.IsEnabled);
        dialog.Close();
    }

    [AvaloniaFact]
    public void Minus_and_plus_step_from_the_last_valid_value_and_stop_at_the_bounds()
    {
        var config = new AppConfig { AutosaveDelaySeconds = 0.5 };
        var dialog = new SettingsDialog(config, _ => true);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var autosave = Named<NumberField>(dialog, "AutosaveDelay");

        autosave.Input.Text = "x";
        autosave.Increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("0.75", autosave.Input.Text);
        Assert.False(autosave.Error.IsVisible);
        Assert.Equal(0.75, config.AutosaveDelaySeconds);

        autosave.Decrease.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        autosave.Decrease.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("0.25", autosave.Input.Text);
        Assert.Equal(0.25, config.AutosaveDelaySeconds);
        Assert.False(autosave.Decrease.IsEnabled);
        Assert.True(autosave.Increase.IsEnabled);

        var spacing = Named<NumberField>(dialog, "TextStyleLineSpacing");
        spacing.Increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new AppConfig().TextStyles[0].LineSpacing + 0.1, config.TextStyles[0].LineSpacing, 9);
        dialog.Close();
    }

    [AvaloniaFact]
    public void Selecting_a_style_is_not_an_edit_of_its_font_family()
    {
        var config = new AppConfig();
        // A family the box's cleanup would change, so a load read as an edit would rewrite it.
        config.TextStyles[1].FontFamily = "  Menlo  ";
        var dialog = new SettingsDialog(config, _ => true);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));

        Named<ListBox>(dialog, "TextStylesList").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("  Menlo  ", Named<TextBox>(dialog, "TextStyleFontFamily").Text);
        Assert.Equal("  Menlo  ", config.TextStyles[1].FontFamily);
        Assert.False(save.IsEnabled);

        // The next change is the user's.
        Named<TextBox>(dialog, "TextStyleFontFamily").Text = "  Menlo  x";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Menlo  x", config.TextStyles[1].FontFamily);
        Assert.True(save.IsEnabled);
        dialog.Close();
    }

    [AvaloniaFact]
    public void Showing_a_value_is_not_typing_even_once_the_box_reports_the_change()
    {
        var field = new NumberField(SettingsValidator.FontSize, 1);
        var window = new Window { Content = field };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var events = 0;
        field.Committed += _ => events++;
        field.Changed += () => events++;

        field.Show(12);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, events);

        // The next change is the user's, even when it types back what was shown.
        field.Input.Text = "13";
        Dispatcher.UIThread.RunJobs();
        field.Input.Text = "12";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, events);
        Assert.Equal(12, field.Value);
        window.Close();
    }

    [AvaloniaFact]
    public void Selecting_another_style_shows_its_numbers_clears_a_message_and_frees_save()
    {
        var config = new AppConfig();
        config.TextStyles[1].FontSize = 22;
        // One family for both, so only the numbers change when the selection does.
        config.TextStyles[1].FontFamily = config.TextStyles[0].FontFamily;
        var dialog = new SettingsDialog(config, _ => true);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var save = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "ok"));
        // A valid change first, so Save has something to save once the message goes.
        Named<NumberField>(dialog, "TextStyleLineSpacing").Input.Text = "2";
        var size = Named<NumberField>(dialog, "TextStyleFontSize");
        size.Input.Text = "7";
        Dispatcher.UIThread.RunJobs();
        Assert.True(size.HasError);
        Assert.False(save.IsEnabled);

        Named<ListBox>(dialog, "TextStylesList").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("22", size.Input.Text);
        Assert.False(size.HasError);
        Assert.True(save.IsEnabled);
        Assert.Equal(new AppConfig().TextStyles[0].FontSize, config.TextStyles[0].FontSize);
        Assert.Equal(2, config.TextStyles[0].LineSpacing);
        dialog.Close();
    }

    [AvaloniaFact]
    public void A_styles_row_says_what_was_typed_even_when_no_such_font_is_installed()
    {
        var config = new AppConfig();
        var dialog = new SettingsDialog(config, _ => true, _ => Task.FromResult(true), askBeforeDiscarding: Discard);
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
        }, askBeforeDiscarding: Discard);
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
        var size = Named<NumberField>(dialog, "TextStyleFontSize");

        var buttons = size.GetVisualDescendants().OfType<RepeatButton>()
            .OrderBy(button => button.TranslatePoint(default, size)!.Value.X)
            .ToList();
        var glyphs = buttons
            .Select(button => button.GetVisualDescendants().OfType<PathIcon>().Single().Data!.Bounds)
            .ToList();

        Assert.Equal([size.Decrease, size.Increase], buttons);
        Assert.Equal(["Decrease", "Increase"], buttons.Select(button => Avalonia.Automation.AutomationProperties.GetName(button)));
        // A bar as wide as the square cross beside it: minus, then plus.
        Assert.Equal(glyphs[0].Width, glyphs[1].Width);
        Assert.Equal(glyphs[1].Width, glyphs[1].Height);
        Assert.True(glyphs[0].Height < glyphs[1].Height / 4, $"The minus is {glyphs[0].Height:0.##} tall.");
        // The buttons stand as tall as the field, and the field keeps room for a value such as 1.25.
        Assert.All(buttons, button => Assert.Equal(size.Input.Bounds.Height, button.Bounds.Height, 0.5));
        Assert.True(size.Input.Bounds.Width >= 48, $"The text box is {size.Input.Bounds.Width:0.#} wide.");
        dialog.Close();
    }

    private static string RowLabel(ListBox list, int index) =>
        list.ContainerFromIndex(index)!.GetLogicalDescendants().OfType<TextBlock>().First().Text!;

    private static bool[] DefaultBadges(ListBox list) =>
        Enumerable.Range(0, list.ItemCount)
            .Select(index => list.ContainerFromIndex(index)!.GetLogicalDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("badge")).IsVisible)
            .ToArray();

    private static Task<bool> Discard() => Task.FromResult(true);

    private static Button Cancel(SettingsDialog dialog) =>
        dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Tag, "cancel"));

    private static T Named<T>(SettingsDialog dialog, string name) where T : Control =>
        dialog.GetLogicalDescendants().OfType<T>().Single(control => control.Name == name);
}
