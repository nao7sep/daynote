using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The catalog is the single source of truth for both the live accelerators and the help modal, so a
/// displayed label can never describe a binding that does not exist. These guard that: gesture and
/// action travel together, every action appears exactly once, no chord is bound twice, and each label's
/// modifiers match its gesture.
/// </summary>
public sealed class ShortcutCatalogTests
{
    private static (IReadOnlyList<ShortcutItem> items, KeyModifiers cmd) BuildCatalog()
    {
        // A real TopLevel resolves the platform command modifier the same way the window does.
        var window = new Window();
        window.Show();
        return (ShortcutCatalog.Build(window), ShortcutCatalog.CommandModifier(window));
    }

    [AvaloniaFact]
    public void Command_rows_carry_both_a_gesture_and_an_action_display_rows_carry_neither()
    {
        var (items, _) = BuildCatalog();
        foreach (var item in items)
        {
            Assert.Equal(item.Gesture is null, item.Action is null);
        }
    }

    [AvaloniaFact]
    public void Every_action_appears_exactly_once()
    {
        var (items, _) = BuildCatalog();
        var actions = items.Where(i => i.Action is not null).Select(i => i.Action!.Value).ToList();
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            Assert.Equal(1, actions.Count(a => a == action));
        }
    }

    [AvaloniaFact]
    public void No_chord_is_bound_twice()
    {
        var (items, _) = BuildCatalog();
        var gestures = items.Where(i => i.Gesture is not null).Select(i => i.Gesture!).ToList();
        var distinct = gestures.Select(g => (g.Key, g.KeyModifiers)).Distinct().Count();
        Assert.Equal(gestures.Count, distinct);
    }

    [AvaloniaFact]
    public void Each_command_label_and_gesture_agree_on_modifiers()
    {
        var (items, cmd) = BuildCatalog();
        foreach (var item in items.Where(i => i.Gesture is not null))
        {
            Assert.StartsWith("Cmd/Ctrl+", item.Label);
            Assert.True(item.Gesture!.KeyModifiers.HasFlag(cmd)); // the gesture carries the platform command modifier
            Assert.Equal(item.Gesture.KeyModifiers.HasFlag(KeyModifiers.Shift), item.Label.Contains("Shift+"));
        }
    }
}
