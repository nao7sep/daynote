using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class CompositeInteractionTests : WindowTest
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Menu_navigation_keeps_its_target_while_an_earlier_item_is_hovered(bool keyboard)
    {
        var window = Show(new MainWindow());
        var trigger = window.GetVisualDescendants().OfType<Button>().Single(button => button.Flyout is MenuFlyout);
        var flyout = (MenuFlyout)trigger.Flyout!;
        var items = flyout.Items.OfType<MenuItem>().ToArray();
        var clicked = -1;
        for (var index = 0; index < items.Length; index++)
        {
            var command = index;
            items[index].Click += (_, _) => clicked = command;
        }

        try
        {
            Open(window, trigger, keyboard);
            Assert.True(flyout.IsOpen);
            Assert.True(items[0].IsFocused);
            var popup = TopLevel.GetTopLevel(items[0])!;
            Key(popup, PhysicalKey.ArrowDown);
            Assert.True(items[1].IsFocused);
            Key(popup, PhysicalKey.ArrowDown);
            Assert.True(items[2].IsFocused);
            Hover(items[0]);
            Assert.True(items[0].IsPointerOver);
            Assert.True(items[2].IsFocused);
            Assert.True(items[2].IsSelected);
            Assert.False(items[0].IsSelected);
            Key(popup, PhysicalKey.ArrowDown);
            Assert.True(items[3].IsFocused);
            Key(popup, PhysicalKey.Enter);
            Assert.Equal(3, clicked);
            Assert.False(flyout.IsOpen);
        }
        finally
        {
            flyout.Hide();
        }
    }

    [AvaloniaFact]
    public void A_menu_click_acts_on_the_clicked_item_after_keyboard_navigation()
    {
        var window = Show(new MainWindow());
        var trigger = window.GetVisualDescendants().OfType<Button>().Single(button => button.Flyout is MenuFlyout);
        var flyout = (MenuFlyout)trigger.Flyout!;
        var items = flyout.Items.OfType<MenuItem>().ToArray();
        var clicked = -1;
        for (var index = 0; index < items.Length; index++)
        {
            var command = index;
            items[index].Click += (_, _) => clicked = command;
        }

        try
        {
            Open(window, trigger, keyboard: true);
            var popup = TopLevel.GetTopLevel(items[0])!;
            Key(popup, PhysicalKey.ArrowDown);
            Key(popup, PhysicalKey.ArrowDown);
            Click(items[0]);
            Assert.Equal(0, clicked);
            Assert.False(flyout.IsOpen);
        }
        finally
        {
            flyout.Hide();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_picker_starts_at_its_selection_and_hover_does_not_change_the_keyboard_target(bool keyboard)
    {
        var picker = new ComboBox { ItemsSource = new[] { "First", "Second", "Third", "Fourth" }, SelectedIndex = 1 };
        var window = Show(new Window { Width = 300, Height = 220, Content = picker });
        try
        {
            Open(window, picker, keyboard);
            Assert.True(picker.IsDropDownOpen);
            var selected = (ComboBoxItem)picker.ContainerFromIndex(1)!;
            Assert.True(selected.IsSelected);
            Assert.True(selected.IsFocused);
            var popup = TopLevel.GetTopLevel(selected)!;
            Key(popup, PhysicalKey.ArrowDown);
            var target = (ComboBoxItem)picker.ContainerFromIndex(2)!;
            Assert.True(target.IsFocused);
            Hover((ComboBoxItem)picker.ContainerFromIndex(0)!);
            Assert.Equal(1, picker.SelectedIndex);
            Assert.True(target.IsFocused);
            Key(popup, PhysicalKey.ArrowDown);
            Assert.True(picker.ContainerFromIndex(3)!.IsFocused);
            Key(popup, PhysicalKey.Enter);
            Assert.Equal(3, picker.SelectedIndex);
            Assert.False(picker.IsDropDownOpen);
        }
        finally
        {
            picker.IsDropDownOpen = false;
        }
    }

    [AvaloniaFact]
    public void A_list_hover_preserves_selection_and_keyboard_focus()
    {
        var list = new ListBox { ItemsSource = new[] { "First", "Second", "Third" }, SelectedIndex = 1 };
        var other = new TextBox();
        Show(new Window { Width = 300, Height = 220, Content = new StackPanel { Children = { list, other } } });
        var selected = (ListBoxItem)list.ContainerFromIndex(1)!;
        selected.Focus(NavigationMethod.Directional);
        Hover((ListBoxItem)list.ContainerFromIndex(0)!);
        Assert.True(selected.IsFocused);
        Assert.True(selected.IsSelected);
        Assert.Equal(1, list.SelectedIndex);
        other.Focus();
        Hover(selected);
        Assert.True(other.IsFocused);
        Assert.True(selected.IsSelected);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Menu_hover_and_keyboard_target_keep_distinct_surfaces_without_moving(bool dark)
    {
        var window = Show(new MainWindow { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light });
        var trigger = window.GetVisualDescendants().OfType<Button>().Single(button => button.Flyout is MenuFlyout);
        var flyout = (MenuFlyout)trigger.Flyout!;
        var items = flyout.Items.OfType<MenuItem>().ToArray();
        try
        {
            Open(window, trigger, keyboard: true);
            var bounds = items.Select(item => item.Bounds).ToArray();
            Assert.Equal(Brush(items[0], "UtilityHoverBrush"), Fill(Highlight(items[0]).Background));
            var popup = TopLevel.GetTopLevel(items[0])!;
            Key(popup, PhysicalKey.ArrowDown);
            Key(popup, PhysicalKey.ArrowDown);
            Hover(items[0]);
            Assert.Equal(Brush(items[0], "UtilityBrush"), Fill(Highlight(items[0]).Background));
            Assert.Equal(Brush(items[2], "UtilityHoverBrush"), Fill(Highlight(items[2]).Background));
            Assert.True(items[2].IsSelected);
            Evidence(popup, $"menu-{(dark ? "dark" : "light")}-separate-hover");
            Hover(items[2]);
            Assert.Equal(Brush(items[2], "UtilityPressedBrush"), Fill(Highlight(items[2]).Background));
            Assert.True(items[2].IsSelected);
            Assert.Equal(bounds, items.Select(item => item.Bounds).ToArray());
            Evidence(popup, $"menu-{(dark ? "dark" : "light")}-combined-hover");
        }
        finally
        {
            flyout.Hide();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_selected_list_row_keeps_its_focus_ring_and_selection_under_hover_and_blur(bool dark)
    {
        var list = new ListBox { ItemsSource = new[] { "First", "Second", "Third" }, SelectedIndex = 1 };
        var editor = new TextBox { Text = "Detail pane" };
        var window = Show(new Window
        {
            Width = 300, Height = 220, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new StackPanel { Children = { list, editor } },
        });
        var selected = (ListBoxItem)list.ContainerFromIndex(1)!;
        selected.Focus(NavigationMethod.Directional);
        var presenter = selected.GetVisualDescendants().OfType<ContentPresenter>().First();
        Assert.Equal(Brush(selected, "ListSelectionBrush"), Fill(presenter.Background));
        var bounds = selected.Bounds;
        Hover(selected);
        Assert.Equal(Brush(selected, "ListSelectionHoverBrush"), Fill(presenter.Background));
        Assert.Contains(":focus-visible", selected.Classes);
        Assert.Same(Application.Current!.FindResource("RowFocusRing"), selected.FocusAdorner);
        Assert.Equal(bounds, selected.Bounds);
        Evidence(window, $"list-{(dark ? "dark" : "light")}-combined-hover");
        editor.Focus();
        Hover((ListBoxItem)list.ContainerFromIndex(0)!);
        Assert.True(selected.IsSelected);
        Assert.Equal(Brush(selected, "ListSelectionBrush"), Fill(presenter.Background));
        Assert.True(editor.IsFocused);
    }

    private static Border Highlight(MenuItem item) => item.GetVisualDescendants().OfType<Border>()
        .Single(border => border.Name == "PART_LayoutRoot");

    private static Color Fill(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Color Brush(Control control, string resource) =>
        Fill((IBrush)control.FindResource(control.ActualThemeVariant, resource)!);

    private static void Evidence(TopLevel root, string name)
    {
        if (System.Environment.GetEnvironmentVariable("DAYNOTE_LAYOUT_EVIDENCE") is not { Length: > 0 } directory)
            return;
        Directory.CreateDirectory(directory);
        using var frame = root.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, $"{name}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private static void Open(Window window, Control trigger, bool keyboard)
    {
        if (keyboard)
        {
            trigger.Focus(NavigationMethod.Tab);
            Key(window, PhysicalKey.Space);
        }
        else
        {
            Click(trigger);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(TopLevel root, PhysicalKey key)
    {
        root.KeyPressQwerty(key, RawInputModifiers.None);
        root.KeyReleaseQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Hover(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        root.MouseMove(control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
