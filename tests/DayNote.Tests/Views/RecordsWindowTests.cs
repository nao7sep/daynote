using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DayNote.Tests.ViewModels;
using DayNote.ViewModels;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

public sealed class RecordsWindowTests : WindowTest
{
    private readonly FakeRecordsSource _source = new();
    private readonly FakeRecordsHost _host = new();
    private readonly ManualScheduler _timers = new();

    private RecordsWindow Create() =>
        new(new RecordsWindowViewModel(_source, _host, new RecordingLogger(), _timers.Schedule));

    private static ColumnDefinition ListColumn(RecordsWindow window) =>
        window.FindControl<Grid>("PaneGrid")!.ColumnDefinitions[0];

    [AvaloniaFact]
    public void The_list_opens_at_its_saved_width_with_the_newest_records()
    {
        _host.RecordsListWidth = 450;
        _source.Add(3);

        var window = Show(Create());

        Assert.Equal(450, ListColumn(window).ActualWidth, precision: 0);
        var list = window.FindControl<ListBox>("RecordsList")!;
        Assert.Equal(3, list.ItemCount);
        Assert.Equal(RecordsLayout.MinWidth, window.MinWidth);
        Assert.True(window.MinHeight > RecordsLayout.ListBodyMin);
    }

    [AvaloniaFact]
    public void Only_a_finished_drag_saves_the_width_and_a_narrow_window_keeps_the_intent()
    {
        _host.RecordsListWidth = 450;
        var window = Show(Create());

        window.Width = 800;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(366, ListColumn(window).ActualWidth, precision: 0);
        Assert.Empty(_host.SavedListWidths);

        window.Width = 1100;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(450, ListColumn(window).ActualWidth, precision: 0);

        ListColumn(window).Width = new GridLength(500);
        Dispatcher.UIThread.RunJobs();
        window.FindControl<GridSplitter>("ListSplitter")!.RaiseEvent(
            new VectorEventArgs { RoutedEvent = Thumb.DragCompletedEvent, Vector = new Vector(50, 0) });

        Assert.Equal([500.0], _host.SavedListWidths);
    }

    [AvaloniaFact]
    public void A_saved_placement_on_a_screen_is_applied_before_the_window_shows_and_saved_as_it_closes()
    {
        var probe = Show(new Window());
        var area = (probe.Screens.ScreenFromWindow(probe) ?? probe.Screens.Primary!).WorkingArea;
        var saved = new WindowPlacement(area.X + 40, area.Y + 30, 900, 640, Maximized: false);
        _host.RecordsWindowPlacement = saved;

        var window = Create();
        window.RestoreWindowGeometry();
        Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
        Assert.Equal(new PixelPoint(saved.X, saved.Y), window.Position);
        Assert.Equal(900, window.Width);
        Assert.Equal(640, window.Height);

        Show(window);
        window.Close();
        Dispatcher.UIThread.RunJobs();

        var written = Assert.Single(_host.SavedPlacements);
        Assert.Equal(900, written.Width);
        Assert.Equal(640, written.Height);
        Assert.False(written.Maximized);
    }

    [AvaloniaFact]
    public void A_placement_no_screen_shows_leaves_the_designed_size()
    {
        _host.RecordsWindowPlacement = new WindowPlacement(-100_000, -100_000, 900, 640, Maximized: false);

        var window = Create();
        window.RestoreWindowGeometry();

        Assert.NotEqual(new PixelPoint(-100_000, -100_000), window.Position);
        Assert.Equal(1100, window.Width);
        Assert.Equal(760, window.Height);
    }

    [AvaloniaFact]
    public void Arrow_keys_move_the_selection_and_the_detail_follows_it()
    {
        _source.Add(3);
        var window = Show(Create());
        var list = window.FindControl<ListBox>("RecordsList")!;
        var vm = (RecordsWindowViewModel)window.DataContext!;

        list.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        var first = (ListBoxItem)list.ContainerFromIndex(0)!;
        first.Focus(NavigationMethod.Tab);
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, vm.SelectedRecord!.Record.Id);
        Assert.Equal("Message 2", vm.Detail!.Title);
        Assert.Equal(1, list.SelectedIndex);
    }

    [AvaloniaFact]
    public void Scrolling_to_the_end_of_the_list_reads_the_next_page()
    {
        _source.Add(150);
        var window = Show(Create());
        var list = window.FindControl<ListBox>("RecordsList")!;
        Assert.Equal(100, list.ItemCount);

        var scroll = (ScrollViewer)list.Scroll!;
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(150, list.ItemCount);
    }

    [AvaloniaFact]
    public void Closing_the_window_stops_it_following_new_records()
    {
        var window = Show(Create());
        Assert.Equal(1, _source.Subscribers);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, _source.Subscribers);
    }
}
