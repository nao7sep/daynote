using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DayNote.Desktop.Views;

/// <summary>
/// Keyboard-shortcuts help. Renders the <see cref="ShortcutCatalog"/> it is handed — the same source
/// the live accelerators are built from — grouped into sections, each a rounded card with the
/// description on the left and the key on the right as a keycap. The window Title becomes the header
/// (via <see cref="DialogBase"/>). Read-only: no draft state.
/// </summary>
public sealed class ShortcutsDialog : DialogBase
{
    public ShortcutsDialog(IReadOnlyList<ShortcutItem> shortcuts)
    {
        Width = 460;
        Title = "Keyboard Shortcuts";

        var sections = new StackPanel { Spacing = 16 };

        foreach (var group in ShortcutCatalog.GroupOrder)
        {
            var rows = shortcuts.Where(s => s.Group == group).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            sections.Children.Add(new TextBlock
            {
                Text = ShortcutCatalog.GroupHeader(group),
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                Foreground = PaletteBrush.Resolve("TextSecondaryBrush"),
                Margin = new Thickness(2, 0, 0, 6),
            });
            sections.Children.Add(BuildCard(rows));
        }

        SetContent(sections);
        var buttons = SetButtons([("Close", "ok", true)]);
        SetInitialFocus(buttons["ok"]);
    }

    // A rounded card per section holding its rows, with a 1px divider between them (none after the last).
    private static Border BuildCard(IReadOnlyList<ShortcutItem> rows)
    {
        var stack = new StackPanel();
        for (var i = 0; i < rows.Count; i++)
        {
            stack.Children.Add(BuildRow(rows[i]));
            if (i < rows.Count - 1)
            {
                stack.Children.Add(new Border { Height = 1, Background = PaletteBrush.Resolve("BorderBrush") });
            }
        }

        return new Border
        {
            BorderBrush = PaletteBrush.Resolve("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = PaletteBrush.Resolve("SurfaceBrush"),
            Padding = new Thickness(14, 4),
            Child = stack,
        };
    }

    // Description on the left (wrapping), key on the right.
    private static Grid BuildRow(ShortcutItem item)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 18,
            Margin = new Thickness(0, 10),
        };

        var description = new TextBlock
        {
            Text = item.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = PaletteBrush.Resolve("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(description, 0);
        grid.Children.Add(description);

        Control key = item.ShowAsKeycap ? Keycap(item.Label) : PlainAffordance(item.Label);
        Grid.SetColumn(key, 1);
        grid.Children.Add(key);

        return grid;
    }

    private static Border Keycap(string label) => new()
    {
        Background = PaletteBrush.Resolve("AppBackgroundBrush"),
        BorderBrush = PaletteBrush.Resolve("BorderBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(8, 3),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Foreground = PaletteBrush.Resolve("TextPrimaryBrush"),
        },
    };

    private static TextBlock PlainAffordance(string label) => new()
    {
        Text = label,
        FontWeight = FontWeight.SemiBold,
        FontSize = 12,
        Foreground = PaletteBrush.Resolve("TextSecondaryBrush"),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
