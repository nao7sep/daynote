using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace DayNote.Views;

/// <summary>
/// A menu item without a keyboard gesture ends as close to its label as it begins. Fluent's MenuItem
/// template keeps a column for the gesture text (<c>PART_InputGestureText</c>) with a 24px leading
/// margin, even when there is no gesture to show, so every such menu carried 24px of blank space
/// after its widest label. The margin is set by the template, which outranks any style, so a style
/// cannot remove it; a local value does, and is set here only while the item has no gesture. An
/// item with a gesture keeps the template's margin, the space between its label and its gesture.
/// The label, its access key, keyboard navigation and automation are untouched: only the empty
/// column's margin changes.
/// </summary>
internal static class MenuItemGestureSpace
{
    private const string GestureTextPart = "PART_InputGestureText";

    /// <summary>
    /// Applies the rule to every menu item in the process, flyouts and context menus included.
    /// Called once, when the application initializes.
    /// </summary>
    internal static void Install()
    {
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<MenuItem>(
            (item, e) => Apply(item, e.NameScope.Find<TextBlock>(GestureTextPart)));
        MenuItem.InputGestureProperty.Changed.AddClassHandler<MenuItem>(
            (item, _) => Apply(item, GestureText(item)));
    }

    private static void Apply(MenuItem item, TextBlock? gestureText)
    {
        if (gestureText is null)
            return;
        if (item.InputGesture is null)
            gestureText.Margin = default;
        else
            gestureText.ClearValue(Layoutable.MarginProperty);
    }

    private static TextBlock? GestureText(MenuItem item) =>
        item.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(text => text.Name == GestureTextPart && ReferenceEquals(text.TemplatedParent, item));
}
