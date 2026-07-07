using System.Windows.Input;

using DayNote.ViewModels;

namespace DayNote.Views;

/// <summary>
/// Routes a keyboard <see cref="ShortcutAction"/> to the view-model command it runs.
/// <see cref="ShortcutAction.FilterNotes"/> is the one action the window handles itself (it
/// focuses a control), so it maps to no command here. Pulled out of the window so a test can
/// assert every action is routed — the previous in-window switch's <c>default</c> arm let a
/// newly-added action silently no-op.
/// </summary>
public static class ShortcutRouter
{
    public static ICommand? CommandFor(MainWindowViewModel vm, ShortcutAction action) => action switch
    {
        ShortcutAction.NewBinder => vm.NewBinderCommand,
        ShortcutAction.OpenBinder => vm.OpenBinderCommand,
        ShortcutAction.SaveNow => vm.SaveNowCommand,
        ShortcutAction.CloseBinder => vm.CloseBinderCommand,
        ShortcutAction.NewNote => vm.NewNoteCommand,
        ShortcutAction.CycleTextStyle => vm.CycleTextStyleCommand,
        ShortcutAction.OpenSettings => vm.OpenSettingsCommand,
        ShortcutAction.ShowShortcuts => vm.OpenShortcutsCommand,
        ShortcutAction.FilterNotes => null, // handled in the view: focuses the notes filter box
        _ => null,
    };
}
