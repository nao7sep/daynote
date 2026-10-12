using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;

namespace DayNote.Views;

/// <summary>
/// Keeps Avalonia's menu navigation, commands and popup lifetime. Its default interaction handler
/// changes the navigation selection on pointer entry/exit, so moving across an earlier command can
/// redirect the next arrow. In DayNote's command menu only deliberate focus or a click changes the
/// target; the native pointer-over state still provides the hover appearance.
/// </summary>
public sealed class CommandMenuFlyout : MenuFlyout
{
    protected override Control CreatePresenter() => new MenuFlyoutPresenter(new Interaction())
    {
        ItemsSource = Items,
        [!ItemsControl.ItemTemplateProperty] = this[!ItemTemplateProperty],
        [!ItemsControl.ItemContainerThemeProperty] = this[!ItemContainerThemeProperty],
    };

    private sealed class Interaction() : DefaultMenuInteractionHandler(isContextMenu: true)
    {
        protected override void PointerEntered(object? sender, RoutedEventArgs e) { }
        protected override void PointerExited(object? sender, RoutedEventArgs e) { }
    }
}
