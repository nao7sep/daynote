using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// <see cref="ListReorder{T}.TryMoveByOffset"/> is the transaction the row's Move Up / Move Down
/// menu items run, and it is also what OnKeyDown now calls for the Cmd/Ctrl+Shift+Up/Down chord — so
/// this is the chord's own coverage, reached through the method both paths now share.
/// </summary>
public sealed class ListReorderTryMoveTests : WindowTest
{
    private sealed class Item(string name)
    {
        public string Name { get; } = name;
    }

    private static ListBox MakeList(ObservableCollection<Item> items) => new() { ItemsSource = items };

    [AvaloniaFact]
    public void Moves_the_item_one_slot_commits_once_and_follows_it()
    {
        var a = new Item("A");
        var b = new Item("B");
        var c = new Item("C");
        var items = new ObservableCollection<Item> { a, b, c };
        var window = Show(new Window { Content = MakeList(items) });
        var box = (ListBox)window.Content!;
        var commits = 0;

        var reorder = new ListReorder<Item>(
            box,
            canReorder: null,
            move: (item, target) =>
            {
                items.Remove(item);
                items.Insert(items.IndexOf(target), item);
                return true;
            },
            commit: () => commits++,
            snapshot: () => [.. items],
            restore: _ => true);

        Assert.True(reorder.TryMoveByOffset(b, -1));

        Assert.Equal([b, a, c], items);
        Assert.Equal(1, commits);
        Assert.Same(b, box.SelectedItem);
    }

    [AvaloniaFact]
    public void Is_a_no_op_at_the_top_edge()
    {
        var items = new ObservableCollection<Item> { new("A"), new("B") };
        var window = Show(new Window { Content = MakeList(items) });
        var box = (ListBox)window.Content!;
        var moved = false;

        var reorder = new ListReorder<Item>(
            box,
            canReorder: null,
            move: (_, _) => { moved = true; return true; },
            commit: () => { },
            snapshot: () => [.. items],
            restore: _ => true);

        Assert.False(reorder.TryMoveByOffset(items[0], -1));
        Assert.False(moved);
    }

    [AvaloniaFact]
    public void Is_a_no_op_at_the_bottom_edge()
    {
        var items = new ObservableCollection<Item> { new("A"), new("B") };
        var window = Show(new Window { Content = MakeList(items) });
        var box = (ListBox)window.Content!;
        var moved = false;

        var reorder = new ListReorder<Item>(
            box,
            canReorder: null,
            move: (_, _) => { moved = true; return true; },
            commit: () => { },
            snapshot: () => [.. items],
            restore: _ => true);

        Assert.False(reorder.TryMoveByOffset(items[1], 1));
        Assert.False(moved);
    }

    [AvaloniaFact]
    public void Defers_to_the_owner_when_reordering_is_locked()
    {
        var items = new ObservableCollection<Item> { new("A"), new("B") };
        var window = Show(new Window { Content = MakeList(items) });
        var box = (ListBox)window.Content!;
        var moved = false;

        var reorder = new ListReorder<Item>(
            box,
            canReorder: () => false,
            move: (_, _) => { moved = true; return true; },
            commit: () => { },
            snapshot: () => [.. items],
            restore: _ => true);

        Assert.False(reorder.TryMoveByOffset(items[0], 1));
        Assert.False(moved);
    }
}
