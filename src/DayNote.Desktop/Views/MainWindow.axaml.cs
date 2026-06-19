using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DayNote.Desktop.ViewModels;

namespace DayNote.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;

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

    // A single tap opens the tapped notebook (re-opening the already-open one is a no-op in the view
    // model). Keyboard arrows still only move the selection — Enter opens — so arrowing through the
    // list does not churn through open/close. (The list is not a text-entry surface, so no IME guard.)
    private void RecentList_Tapped(object? sender, TappedEventArgs e)
    {
        if (RecentList.SelectedItem is RecentNotebookItemViewModel item && DataContext is MainWindowViewModel vm)
        {
            vm.OpenRecentCommand.Execute(item);
        }
    }

    private void RecentList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter
            && RecentList.SelectedItem is RecentNotebookItemViewModel item
            && DataContext is MainWindowViewModel vm)
        {
            vm.OpenRecentCommand.Execute(item);
            e.Handled = true;
        }
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            NotesFilterBox.Focus();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
