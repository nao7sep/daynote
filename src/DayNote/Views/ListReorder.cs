using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DayNote.Controls;

namespace DayNote.Views;

/// <summary>
/// The small app-owned intention and keyboard mappings around Avalonia's native drag session.
/// </summary>
public static class ListReorder
{
    /// <summary>
    /// Preserves ordinary row selection until the pointer moves far enough to express drag intent.
    /// Avalonia owns all transport behavior after this application-level decision.
    /// </summary>
    public static bool ExceedsDragThreshold(Point origin, Point current, double threshold = 3) =>
        Math.Abs(current.X - origin.X) >= threshold || Math.Abs(current.Y - origin.Y) >= threshold;

    /// <summary>
    /// Returns the one-row move requested by a list-reorder chord. Bare arrows remain owned by the
    /// listbox; exact Cmd/Ctrl+Shift+Up/Down chords are the separate command layer.
    /// </summary>
    public static int KeyboardOffset(Key key, KeyModifiers modifiers)
    {
        var command = modifiers == (KeyModifiers.Meta | KeyModifiers.Shift)
            || modifiers == (KeyModifiers.Control | KeyModifiers.Shift);
        if (!command)
        {
            return 0;
        }

        return key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            _ => 0,
        };
    }

    /// <summary>The platform-labelled help text for the keyboard reorder command.</summary>
    public static string KeyboardLabel(string commandModifierLabel) =>
        $"{commandModifierLabel}+Shift+Up/Down";
}

/// <summary>
/// Drag and keyboard reordering for one <see cref="ListBox"/>. Avalonia's native drag session owns
/// capture, cursor, routing, cancellation, and cleanup; this class owns only the click-versus-drag
/// threshold, the live preview through the owner's move operation, and mapping the session's end to
/// one commit or one restore. Items are identified by reference, never by index or label, and the
/// keyboard chord runs the same move and commit as a drop.
/// </summary>
/// <typeparam name="T">The list's item type.</typeparam>
public sealed class ListReorder<T>
    where T : class
{
    // A serializable application format gives macOS a real pasteboard item; an in-process-only object
    // crashed NSDraggingSession. Each drag carries its own token, so one list ignores another's drag.
    private static readonly DataFormat<string> ReorderFormat =
        DataFormat.CreateStringApplicationFormat("com.nao7sep.daynote.list-reorder");

    private readonly ListBox _list;
    private readonly Func<T, T, bool> _move;
    private readonly Action _commit;
    private readonly Func<IReadOnlyList<T>> _snapshot;
    private readonly Func<IReadOnlyList<T>, bool> _restore;

    private bool _pressed;
    private T? _item;
    private Point? _origin;
    private IReadOnlyList<T>? _startOrder;
    private string? _token;

    /// <param name="list">The list whose rows are reordered.</param>
    /// <param name="move">Moves an item to its target's place as a live, unsaved step.</param>
    /// <param name="commit">Persists the current order once; a no-op when nothing changed.</param>
    /// <param name="snapshot">Captures the owner's full order before a drag, hidden rows included.</param>
    /// <param name="restore">Returns the owner to a snapshot, or false when the list changed meanwhile.</param>
    public ListReorder(
        ListBox list,
        Func<T, T, bool> move,
        Action commit,
        Func<IReadOnlyList<T>> snapshot,
        Func<IReadOnlyList<T>, bool> restore)
    {
        _list = list;
        _move = move;
        _commit = commit;
        _snapshot = snapshot;
        _restore = restore;

        list.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        list.AddHandler(InputElement.PointerReleasedEvent, (_, _) => CancelIntent(), handledEventsToo: true);
        list.PointerCaptureLost += (_, _) => CancelIntent();
        list.DetachedFromVisualTree += (_, _) => CancelIntent();
        list.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        list.AddHandler(DragDrop.DropEvent, OnDragOver);
        list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>True while a native drag session started by this list is running.</summary>
    public bool IsReordering { get; private set; }

    /// <summary>Abandons a press that has not yet become a drag, as when the window deactivates.</summary>
    public void CancelIntent()
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        _item = null;
        _origin = null;
    }

    /// <summary>Selects the item, brings it into view, and optionally gives its row focus.</summary>
    public void Follow(T item, bool restoreFocus)
    {
        var index = _list.Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        _list.SelectedItem = item;
        _list.ScrollIntoView(item);
        _list.UpdateLayout();
        if (restoreFocus)
        {
            (_list.ContainerFromIndex(index) as Control)?.Focus();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // A press on a row's button or text field clicks or edits instead of starting a drag.
        if (e.Source is Visual visual
            && (visual is Button or TextBox || visual.GetVisualAncestors().Any(a => a is Button or TextBox)))
        {
            return;
        }

        if (IsReordering || _pressed
            || !e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed
            || (e.Source as Control)?.DataContext is not T item)
        {
            return;
        }

        // The grabbed item is the active item for the whole transaction. Set this explicitly so a
        // drag that crosses the platform's selection threshold still follows stable identity.
        _list.SelectedItem = item;
        (_list.ContainerFromIndex(_list.Items.IndexOf(item)) as Control)?.Focus();
        _item = item;
        _origin = e.GetPosition(_list);
        _pressed = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed || _origin is not { } origin || _item is not { } item)
        {
            return;
        }

        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed)
        {
            CancelIntent();
            return;
        }

        if (!ListReorder.ExceedsDragThreshold(origin, e.GetPosition(_list))
            || TopLevel.GetTopLevel(_list) is not { } topLevel)
        {
            return;
        }

        // The session starts here, inside the live move, and is triggered by an event built from this
        // pointer's current position with the list as its source. Avalonia 12 takes only a
        // PointerPressedEventArgs, and the platform reads the drag image's origin from it: the press
        // event cannot serve, because by now its own row may have been replaced (selecting a binder
        // opens it, which rebuilds the list) and a detached source resolves to the window's corner.
        _pressed = false;
        var point = e.GetCurrentPoint(topLevel);
        var trigger = new PointerPressedEventArgs(
            _list,
            e.Pointer,
            topLevel,
            point.Position,
            e.Timestamp,
            point.Properties,
            e.KeyModifiers);
        _ = RunDragAsync(trigger, item);
    }

    private async Task RunDragAsync(PointerPressedEventArgs trigger, T item)
    {
        IsReordering = true;
        _startOrder = _snapshot();
        _token = Guid.NewGuid().ToString("N");
        using var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ReorderFormat, _token));

        var result = DragDropEffects.None;
        try
        {
            result = await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Move);
        }
        finally
        {
            Finish(item, commit: result == DragDropEffects.Move);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var isOwnDrag = IsReordering
            && _token is { } token
            && e.DataTransfer.TryGetValue(ReorderFormat) == token;
        if (!isOwnDrag)
        {
            return; // anything else, such as external files, keeps bubbling to its own receiver
        }

        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        if (_item is not { } item
            || (e.Source as Control)?.DataContext is not T target
            || _list.Items.IndexOf(target) < 0)
        {
            return;
        }

        var keepFocus = _list.IsKeyboardFocusWithin;
        if (!ReferenceEquals(item, target) && _move(item, target))
        {
            Follow(item, keepFocus);
        }

        e.DragEffects = DragDropEffects.Move;
    }

    private void Finish(T item, bool commit)
    {
        var keepFocus = _list.IsKeyboardFocusWithin;
        if (commit)
        {
            _commit();
        }
        else if (_startOrder is { } startOrder && !_restore(startOrder))
        {
            // The list changed while captured (for example, a reload). The snapshot no longer
            // describes it, so persist the current order rather than leave display and storage apart.
            _commit();
        }

        _item = null;
        _origin = null;
        _startOrder = null;
        _token = null;
        IsReordering = false;
        Follow(item, keepFocus);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsReordering
            || e.Source is TextBox
            || (TopLevel.GetTopLevel(_list) is { } top && ComposingTextBox.IsFocusedElementComposing(top))
            || _list.SelectedItem is not T item)
        {
            return;
        }

        var keepFocus = _list.IsKeyboardFocusWithin;
        var offset = ListReorder.KeyboardOffset(e.Key, e.KeyModifiers);
        var targetIndex = _list.Items.IndexOf(item) + offset;
        if (offset == 0 || targetIndex < 0 || targetIndex >= _list.ItemCount
            || _list.Items[targetIndex] is not T target || !_move(item, target))
        {
            return;
        }

        // A keyboard move is one complete transaction: the same move as a drag preview, then one
        // commit. The stable item stays selected and, because the list owns focus, follows its row.
        _commit();
        Follow(item, keepFocus);
        e.Handled = true;
    }
}
