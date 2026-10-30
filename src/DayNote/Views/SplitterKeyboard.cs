using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DayNote.Views;

/// <summary>
/// Keyboard resizing for a pane splitter, so a pane can be sized without a pointer: the arrow keys along
/// its axis move it 16 px, a step large enough to see and small enough to place a pane closely, and
/// Home and End move it to either end. The pane's new size is the user's intent, as a drag's is, so a later
/// window resize keeps it. A size saved on its own is saved <see cref="SaveDelay"/> after the last step,
/// each step pushing the save back, as continuous editing saves (unsaved-edits-conventions, Saving while
/// the user works), and at once when focus leaves. The toolkit's own 10 px step neither recorded the intent
/// nor saved it.
/// </summary>
public static class SplitterKeyboard
{
    public const double Step = 16;

    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The pane's size after <paramref name="key"/>, or null for a key the splitter does not take.
    /// <paramref name="paneBefore"/>: the pane lies left of or above the splitter, so moving the splitter
    /// right or down grows it.
    /// </summary>
    public static double? Resize(Key key, Orientation axis, double size, double min, double max, bool paneBefore)
    {
        if (key == Key.Home)
            return paneBefore ? min : max;
        if (key == Key.End)
            return paneBefore ? max : min;

        int? direction = (axis, key) switch
        {
            (Orientation.Horizontal, Key.Right) or (Orientation.Vertical, Key.Down) => 1,
            (Orientation.Horizontal, Key.Left) or (Orientation.Vertical, Key.Up) => -1,
            _ => null,
        };
        if (direction is not { } toward)
            return null;
        return Math.Clamp(size + Step * toward * (paneBefore ? 1 : -1), min, Math.Max(min, max));
    }

    /// <summary>
    /// Handles the keys on <paramref name="splitter"/>: <paramref name="measure"/> gives the pane's current
    /// size and bounds, <paramref name="apply"/> records the new size as the intent and shows it, and
    /// <paramref name="commit"/>, when given, saves it after the last step or once focus leaves.
    /// <paramref name="schedule"/> runs an action once after a delay, and disposing what it returns cancels
    /// it. Returns what saves a pending size at once, for a window that closes before the delay ends.
    /// </summary>
    public static Action Attach(
        GridSplitter splitter, Orientation axis, bool paneBefore,
        Func<(double Size, double Min, double Max)> measure, Action<double> apply, Action? commit = null,
        Func<TimeSpan, Action, IDisposable>? schedule = null)
    {
        schedule ??= (delay, action) => DispatcherTimer.RunOnce(action, delay);
        var pending = false;
        IDisposable? timer = null;
        splitter.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            var (size, min, max) = measure();
            if (Resize(e.Key, axis, size, min, max, paneBefore) is not { } next)
                return;
            apply(next);
            pending = true;
            // A held key repeats its steps before it is released; the save waits for the last of them.
            timer?.Dispose();
            timer = null;
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        void Commit()
        {
            timer?.Dispose();
            timer = null;
            if (!pending)
                return;
            pending = false;
            commit?.Invoke();
        }

        splitter.KeyUp += (_, _) =>
        {
            if (pending)
            {
                timer?.Dispose();
                timer = schedule(SaveDelay, Commit);
            }
        };
        splitter.LostFocus += (_, _) => Commit();
        return Commit;
    }
}
