using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DayNote.Desktop.ViewModels;

namespace DayNote.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;
    private IReadOnlyList<ShortcutItem>? _shortcuts;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // Window size and position are not remembered (see AppState); only the side-pane widths are.
        PaneGrid.ColumnDefinitions[0].Width = new GridLength(vm.RecentPaneWidth);
        PaneGrid.ColumnDefinitions[2].Width = new GridLength(vm.NotesPaneWidth);
        PaneGrid.ColumnDefinitions[6].Width = new GridLength(vm.AttachmentsPaneWidth);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
        {
            _ = vm.InitializeAsync();
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_shutdownComplete && DataContext is MainWindowViewModel vm)
        {
            e.Cancel = true;
            CapturePaneWidths(vm);
            try
            {
                await vm.ShutdownAsync();
            }
            finally
            {
                _shutdownComplete = true;
                Close();
            }

            return;
        }

        base.OnClosing(e);
    }

    private void CapturePaneWidths(MainWindowViewModel vm)
    {
        vm.RecentPaneWidth = RecentPane.Bounds.Width;
        vm.NotesPaneWidth = NotesPane.Bounds.Width;
        vm.AttachmentsPaneWidth = AttachPane.Bounds.Width;
    }

    private void RemoveAttachment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AttachmentItemViewModel item } && DataContext is MainWindowViewModel vm)
        {
            vm.RemoveAttachmentCommand.Execute(item);
        }
    }

    private void Attachment_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: AttachmentItemViewModel item } && DataContext is MainWindowViewModel vm)
        {
            vm.OpenAttachmentCommand.Execute(item);
        }
    }

    // The hamburger menu items are wired in code-behind rather than bound, since a MenuFlyout's popup
    // does not reliably inherit the window's DataContext for command bindings.
    private void Settings_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.OpenSettingsCommand.Execute(null);

    private void Shortcuts_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.OpenShortcutsCommand.Execute(null);

    private void About_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.OpenAboutCommand.Execute(null);

    private void Title_Submitted(object? sender, RoutedEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.Editor.NormalizeTitle();
        BodyBox.Focus();
    }

    private void Title_LostFocus(object? sender, RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.Editor.NormalizeTitle();

    // Built lazily from this window (a TopLevel) so the command modifier resolves to Cmd on macOS.
    private IReadOnlyList<ShortcutItem> Shortcuts => _shortcuts ??= ShortcutCatalog.Build(this);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && TryHandleShortcut(e))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private bool TryHandleShortcut(KeyEventArgs e)
    {
        foreach (var item in Shortcuts)
        {
            if (item.Gesture is { } gesture && item.Action is { } action && gesture.Matches(e))
            {
                return TryRunShortcut(action);
            }
        }

        // F1 is a universal help key in addition to Cmd/Ctrl+/.
        return e.Key == Key.F1 && TryRunShortcut(ShortcutAction.ShowShortcuts);
    }

    private bool TryRunShortcut(ShortcutAction action)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return false;
        }

        switch (action)
        {
            case ShortcutAction.NewNotebook: return Run(vm.NewNotebookCommand);
            case ShortcutAction.OpenNotebook: return Run(vm.OpenNotebookCommand);
            case ShortcutAction.SaveNow: return Run(vm.SaveNowCommand);
            case ShortcutAction.CloseNotebook: return Run(vm.CloseNotebookCommand);
            case ShortcutAction.NewNote: return Run(vm.NewNoteCommand);
            case ShortcutAction.CycleTextStyle: return Run(vm.CycleTextStyleCommand);
            case ShortcutAction.OpenSettings: return Run(vm.OpenSettingsCommand);
            case ShortcutAction.ShowShortcuts: return Run(vm.OpenShortcutsCommand);
            case ShortcutAction.FilterNotes:
                NotesFilterBox.Focus();
                return true;
            default:
                return false;
        }
    }

    // Runs a command if enabled; a disabled command lets the key fall through to default handling.
    private static bool Run(ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }
}
