using System;
using Avalonia;
using Avalonia.Input;

namespace DayNote.Views;

/// <summary>
/// The small app-owned intention and keyboard mappings around Avalonia's native drag session.
/// </summary>
public static class AttachmentReorder
{
    /// <summary>
    /// Preserves ordinary row selection until the pointer moves far enough to express drag intent.
    /// Avalonia owns all transport behavior after this application-level decision.
    /// </summary>
    public static bool ExceedsDragThreshold(Point origin, Point current, double threshold = 3) =>
        Math.Abs(current.X - origin.X) >= threshold || Math.Abs(current.Y - origin.Y) >= threshold;

    /// <summary>
    /// Returns the one-row move requested by a scoped attachment-reorder chord. Bare arrows remain
    /// owned by the listbox; exact Cmd/Ctrl+Shift+Up/Down chords are the separate command layer.
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

    /// <summary>The platform-labelled help text for the scoped keyboard reorder command.</summary>
    public static string KeyboardLabel(string commandModifierLabel) =>
        $"{commandModifierLabel}+Shift+Up/Down";
}
