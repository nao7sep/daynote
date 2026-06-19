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

    private void RecentList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RecentList.SelectedItem is RecentNotebookItemViewModel item && DataContext is MainWindowViewModel vm)
        {
            vm.OpenRecentCommand.Execute(item);
        }
    }

    // Opening a notebook is a deliberate, view-replacing action, so the recent list uses manual
    // activation: Enter on the focused row opens it, mirroring double-tap, rather than opening on
    // mere selection. (The list is not a text-entry surface, so no IME composition guard applies.)
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
