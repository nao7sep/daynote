using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree; // GetPlatformSettings extension on TopLevel

namespace DayNote.Views;

/// <summary>Semantic section a shortcut belongs to; drives the modal's section order and headers.</summary>
public enum ShortcutGroup
{
    Binders,
    Notes,
    Editor,
    Navigation,
    App,
}

/// <summary>
/// Identifies a window-level command accelerator. The window maps each value to a view-model command
/// (or a view action) in <c>MainWindow</c>; display-only rows (Up/Down) carry no action.
/// </summary>
public enum ShortcutAction
{
    NewBinder,
    OpenBinder,
    SaveNow,
    CloseBinder,
    NewNote,
    FilterNotes,
    CycleTextStyle,
    OpenSettings,
    ShowShortcuts,
}

/// <summary>
/// One row of the catalog. <see cref="Gesture"/>/<see cref="Action"/> are set only for command
/// accelerators (which the window both binds and dispatches); display-only rows carry just a label.
/// <see cref="ShowAsKeycap"/> is true for anything naming a key.
/// </summary>
public sealed record ShortcutItem(
    ShortcutGroup Group,
    string Description,
    string Label,
    KeyGesture? Gesture = null,
    ShortcutAction? Action = null,
    bool ShowAsKeycap = true);

/// <summary>
/// The single source of truth for DayNote's keyboard shortcuts: both the live accelerators and the
/// help modal derive from one ordered list, so a displayed label can never describe a binding that
/// does not exist. The catalog owns labels, grouping, and the gesture's platform-resolved modifier;
/// the window maps each <see cref="ShortcutAction"/> to a command.
/// </summary>
public static class ShortcutCatalog
{
    /// <summary>Section order for the modal; only non-empty groups render.</summary>
    public static readonly IReadOnlyList<ShortcutGroup> GroupOrder =
    [
        ShortcutGroup.Binders,
        ShortcutGroup.Notes,
        ShortcutGroup.Navigation,
        ShortcutGroup.Editor,
        ShortcutGroup.App,
    ];

    public static string GroupHeader(ShortcutGroup group) => group switch
    {
        ShortcutGroup.Binders => "Binders",
        ShortcutGroup.Notes => "Notes",
        ShortcutGroup.Editor => "Editor",
        ShortcutGroup.Navigation => "Navigation",
        ShortcutGroup.App => "App",
        _ => group.ToString(),
    };

    /// <summary>
    /// The platform command key — <c>Meta</c> (Cmd) on macOS, <c>Control</c> on Windows/Linux. Resolved
    /// once here so every accelerator binds the right modifier on every platform. Falls back to
    /// <c>Control</c> if platform settings are unavailable.
    /// </summary>
    public static KeyModifiers CommandModifier(TopLevel top) =>
        top.GetPlatformSettings()?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

    /// <summary>
    /// The displayed word for the platform command key — <c>"Cmd"</c> on macOS (where the command
    /// modifier is <c>Meta</c>), <c>"Ctrl"</c> on Windows/Linux. Derived from the same platform signal
    /// as <see cref="CommandModifier"/> so labels show the running platform's single word, never the
    /// combined <c>Cmd/Ctrl</c>.
    /// </summary>
    public static string CommandModifierLabel(TopLevel top) =>
        CommandModifier(top) == KeyModifiers.Meta ? "Cmd" : "Ctrl";

    public static IReadOnlyList<ShortcutItem> Build(TopLevel top)
    {
        var cmd = CommandModifier(top);
        var cmdLabel = CommandModifierLabel(top);
        return new List<ShortcutItem>
        {
            // Binders — file lifecycle: create, open, persist, close.
            Command(ShortcutGroup.Binders, "New binder", cmd, cmdLabel, shift: false, Key.N, "N", ShortcutAction.NewBinder),
            Command(ShortcutGroup.Binders, "Open binder", cmd, cmdLabel, shift: false, Key.O, "O", ShortcutAction.OpenBinder),
            Command(ShortcutGroup.Binders, "Save now", cmd, cmdLabel, shift: false, Key.S, "S", ShortcutAction.SaveNow),
            Command(ShortcutGroup.Binders, "Close binder", cmd, cmdLabel, shift: false, Key.W, "W", ShortcutAction.CloseBinder),

            // Notes — create, delete, find. (Delete is list-scoped, handled by the notes list itself,
            // so it is documented here as a display row rather than a global accelerator.)
            Command(ShortcutGroup.Notes, "New note", cmd, cmdLabel, shift: true, Key.N, "N", ShortcutAction.NewNote),
            Display(ShortcutGroup.Notes, "Delete the selected note", "Delete"),
            Command(ShortcutGroup.Notes, "Filter notes", cmd, cmdLabel, shift: false, Key.F, "F", ShortcutAction.FilterNotes),

            // Navigation — move within the binders and notes lists (selecting a row opens it).
            Display(ShortcutGroup.Navigation, "Move the selection up or down a list", "Up / Down"),

            // Editor — the open note.
            Command(ShortcutGroup.Editor, "Cycle text style", cmd, cmdLabel, shift: false, Key.J, "J", ShortcutAction.CycleTextStyle),

            // App — settings and this help.
            Command(ShortcutGroup.App, "Settings", cmd, cmdLabel, shift: false, Key.OemComma, "Comma", ShortcutAction.OpenSettings),
            Command(ShortcutGroup.App, "Keyboard shortcuts", cmd, cmdLabel, shift: false, Key.OemQuestion, "Slash", ShortcutAction.ShowShortcuts),
        };
    }

    /// <summary>
    /// Builds a command accelerator from one definition so the label and gesture cannot diverge. The
    /// label uses the running platform's single command word (<paramref name="cmdLabel"/>); only the
    /// gesture's modifier is platform-resolved.
    /// </summary>
    private static ShortcutItem Command(
        ShortcutGroup group, string description, KeyModifiers cmd, string cmdLabel, bool shift, Key key, string keyName, ShortcutAction action)
    {
        var label = cmdLabel + "+" + (shift ? "Shift+" : string.Empty) + keyName;
        var modifiers = cmd | (shift ? KeyModifiers.Shift : KeyModifiers.None);
        return new ShortcutItem(group, description, label, new KeyGesture(key, modifiers), action);
    }

    private static ShortcutItem Display(ShortcutGroup group, string description, string label) =>
        new(group, description, label);
}
