using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Desktop.ViewModels;

namespace DayNote.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;
    private IReadOnlyList<ShortcutItem>? _shortcuts;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // Files dropped on the attachments pane are added like the Add button, with a hover highlight.
        AttachPane.AddHandler(DragDrop.DragOverEvent, OnAttachDragOver);
        AttachPane.AddHandler(DragDrop.DragLeaveEvent, OnAttachDragLeave);
        AttachPane.AddHandler(DragDrop.DropEvent, OnAttachDrop);
    }

    private void OnAttachDragOver(object? sender, DragEventArgs e)
    {
        var accept = DataContext is MainWindowViewModel { Editor.HasNote: true } && e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsAttachmentDropActive = accept;
        }

        e.Handled = true;
    }

    private void OnAttachDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsAttachmentDropActive = false;
        }
    }

    private void OnAttachDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.IsAttachmentDropActive = false;
        var paths = e.DataTransfer.TryGetFiles()?
            .OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        if (paths is { Count: > 0 })
        {
            vm.AddDroppedFiles(paths);
        }

        e.Handled = true;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Derive the window minimum from the live pane-Grid columns plus the fixed chrome (see
        // WindowMetrics) rather than a hand-typed constant, so the window can never be shrunk small
        // enough to hide a pane, the toolbar, or the status bar — and so adding, removing, or
        // resizing a column moves the minimum with it. Reading the column MinWidths from the live
        // grid (not a copy) is what keeps the window minimum from drifting away from the columns.
        // Only the four content columns carry a MinWidth; the splitter columns are Auto with none,
        // so a 0 MinWidth contributes nothing and is harmless to include.
        MinWidth = WindowMetrics.MinWidthFor(PaneGrid.ColumnDefinitions.Select(c => c.MinWidth));

        // The tallest pane's content minimum (the editor's, the largest of the four) drives the
        // height; read it live so the XAML stays the single source of truth.
        MinHeight = WindowMetrics.MinHeightFor(EditorPaneContentMinHeight());
    }

    // The editor pane's content root carries the tallest pane MinHeight in MainWindow.axaml; read
    // it back so WindowMetrics derives the window height from the same value the layout enforces.
    private double EditorPaneContentMinHeight() =>
        EditorPane.Child is Control content ? content.MinHeight : 0;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        // Window size and position are not remembered (see AppState); only the pane proportions are.
        // Each saved width is restored as its column's star WEIGHT, so the four content columns keep
        // their proportions across launches while still auto-shrinking with the window. Flooring the
        // weight at the column's own MinWidth keeps a stale state.json (e.g. a width saved before a
        // MinWidth was raised) from starting a pane below its declared minimum — the same minimum the
        // GridSplitters and the derived window minimum honour.
        PaneGrid.ColumnDefinitions[0].Width = RestoredPaneWidth(0, vm.RecentPaneWidth);
        PaneGrid.ColumnDefinitions[2].Width = RestoredPaneWidth(2, vm.NotesPaneWidth);
        PaneGrid.ColumnDefinitions[4].Width = RestoredPaneWidth(4, vm.EditorPaneWidth);
        PaneGrid.ColumnDefinitions[6].Width = RestoredPaneWidth(6, vm.AttachmentsPaneWidth);
    }

    private GridLength RestoredPaneWidth(int column, double savedWidth) =>
        new(Math.Max(savedWidth, PaneGrid.ColumnDefinitions[column].MinWidth), GridUnitType.Star);

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
        vm.EditorPaneWidth = EditorPane.Bounds.Width;
        vm.AttachmentsPaneWidth = AttachPane.Bounds.Width;
    }

    private void RemoveAttachment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AttachmentItemViewModel item } && DataContext is MainWindowViewModel vm)
        {
            vm.RemoveAttachmentCommand.Execute(item);
        }
    }

    // The inline "✕" on a note row deletes that specific note (not necessarily the selected one).
    private void DeleteNoteRow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: NoteListItemViewModel item } && DataContext is MainWindowViewModel vm)
        {
            vm.DeleteNoteCommand.Execute(item);
        }
    }

    // The inline "✕" removes a binder from the list (closing it first if it's the open one).
    private void RemoveBinderRow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RecentBinderItemViewModel item } && DataContext is MainWindowViewModel vm)
        {
            vm.RemoveBinderCommand.Execute(item);
        }
    }

    // Double-tap a binder row to rename its title inline. The first tap of the double already selected
    // (and opened) the binder; this just enters edit mode and focuses the field.
    private void BinderRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: RecentBinderItemViewModel item } || item.IsEditing)
        {
            return;
        }

        item.EditText = item.Title;
        item.IsEditing = true;

        // The editor just became visible; post focus so it is realized first.
        if (sender is Visual visual)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (visual.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } box)
                    {
                        box.Focus();
                        box.SelectAll();
                    }
                },
                DispatcherPriority.Background);
        }
    }

    // Blur applies the title edit. (Enter/Escape are handled in BinderTitle_KeyDown first, which
    // clears IsEditing, so this becomes a no-op for those paths.)
    private void BinderTitle_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RecentBinderItemViewModel item } && DataContext is MainWindowViewModel vm)
        {
            vm.ApplyBinderRename(item, item.EditText);
        }
    }

    private void BinderTitle_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: RecentBinderItemViewModel item } || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.ApplyBinderRename(item, item.EditText);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false; // discard the buffer; Title is untouched
            e.Handled = true;
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
            case ShortcutAction.NewBinder: return Run(vm.NewBinderCommand);
            case ShortcutAction.OpenBinder: return Run(vm.OpenBinderCommand);
            case ShortcutAction.SaveNow: return Run(vm.SaveNowCommand);
            case ShortcutAction.CloseBinder: return Run(vm.CloseBinderCommand);
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
