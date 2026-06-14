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

        Width = vm.InitialWindowWidth;
        Height = vm.InitialWindowHeight;
        PaneGrid.ColumnDefinitions[0].Width = new GridLength(vm.RecentPaneWidth);
        PaneGrid.ColumnDefinitions[2].Width = new GridLength(vm.NotesPaneWidth);
        PaneGrid.ColumnDefinitions[6].Width = new GridLength(vm.AttachmentsPaneWidth);

        if (vm.InitialWindowX is { } x && vm.InitialWindowY is { } y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)x, (int)y);
        }

        if (vm.InitialWindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        EnsureOnScreen();
        if (DataContext is MainWindowViewModel vm)
        {
            _ = vm.InitializeAsync();
        }
    }

    // If the restored position lands outside every connected screen (e.g. a monitor was removed),
    // recentre on the primary screen so the window is not stranded off-screen and unreachable.
    private void EnsureOnScreen()
    {
        var screens = Screens?.All;
        if (screens is null || screens.Count == 0 || screens.Any(s => s.Bounds.Contains(Position)))
        {
            return;
        }

        var target = Screens!.Primary ?? screens[0];
        var area = target.WorkingArea;
        Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - (int)Width) / 2),
            area.Y + Math.Max(0, (area.Height - (int)Height) / 2));
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_shutdownComplete && DataContext is MainWindowViewModel vm)
        {
            e.Cancel = true;
            CaptureGeometry(vm);
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

    private void CaptureGeometry(MainWindowViewModel vm)
    {
        vm.RecentPaneWidth = RecentPane.Bounds.Width;
        vm.NotesPaneWidth = NotesPane.Bounds.Width;
        vm.AttachmentsPaneWidth = AttachPane.Bounds.Width;
        vm.CaptureWindowGeometry(Width, Height, Position.X, Position.Y, WindowState == WindowState.Maximized);
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

    private void Title_Submitted(object? sender, RoutedEventArgs e) => BodyBox.Focus();

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
