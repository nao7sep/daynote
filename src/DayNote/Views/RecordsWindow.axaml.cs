using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using DayNote.Core.Configuration;
using DayNote.I18n;
using DayNote.ViewModels;

namespace DayNote.Views;

/// <summary>
/// The records window: a durable secondary window with its own placement (window-conventions,
/// Placement). The main window opens it, brings it forward when it is open already, and closes it
/// when the app quits; it never keeps the app running.
/// </summary>
public partial class RecordsWindow : Window
{
    /// <summary>The fixed-width face a record's fields are shown in.</summary>
    public static readonly FontFamily FieldsFont = UiFont.ResolveEditor(EditorTextStyle.DefaultFixedWidthFamilies);

    private readonly RecordsWindowViewModel _vm;
    private (int X, int Y, double Width, double Height)? _normalGeometry;

    // The pixel width the user last dragged the list pane to. A window resize re-derives the
    // displayed width from it and never overwrites it.
    private double _listWidthIntent;
    private ScrollViewer? _listScroll;
    private bool _closed;

    // The XAML loader and the designer need a parameterless constructor; the app uses the other.
    public RecordsWindow()
        : this(null!)
    {
    }

    public RecordsWindow(RecordsWindowViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://DayNote/Assets/icon-win.png"));
            Icon = new WindowIcon(iconStream);
        }

        if (vm is null)
        {
            return;
        }

        // The list opens at its saved width, so the first frame already has it.
        _listWidthIntent = vm.ListWidth;
        PaneGrid.ColumnDefinitions[0].Width = new GridLength(RecordsLayout.ListWidth(_listWidthIntent, Width));
        MinWidth = RecordsLayout.MinWidth;

        PositionChanged += (_, _) => RememberNormalGeometryAfterNativeEvents();
        Resized += (_, _) => RememberNormalGeometryAfterNativeEvents();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty)
                ClampListToWindow();
        };
        ListSplitter.AddHandler(Thumb.DragCompletedEvent, OnListSplitterDragCompleted);
        RecordsList.TemplateApplied += (_, _) => AttachListScroll();
        FiltersBand.SizeChanged += (_, _) => MinHeight = RecordsLayout.MinHeight(FiltersBand.Bounds.Height);
    }

    /// <summary>
    /// Applies the saved placement before the window is first shown; a placement no screen can show
    /// leaves the designed size and the system's placement.
    /// </summary>
    public void RestoreWindowGeometry()
    {
        try
        {
            if (_vm.Placement is not { } saved
                || !Screens.All.Any(screen => WindowMetrics.CanRestoreWindowGeometry(
                    saved.X, saved.Y, saved.Width, saved.Height, [screen.WorkingArea])))
            {
                return;
            }

            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(saved.X, saved.Y);
            Width = Math.Max(saved.Width, MinWidth);
            Height = saved.Height;
            _normalGeometry = (saved.X, saved.Y, Width, Height);
            WindowState = WindowMetrics.RestoredWindowState(saved.Maximized, OperatingSystem.IsWindows());
            PaneGrid.ColumnDefinitions[0].Width = new GridLength(RecordsLayout.ListWidth(_listWidthIntent, Width));
        }
        catch (Exception ex)
        {
            // Placement is disposable: the designed size and the system's placement stand.
            Program.Log?.Warn("Records window geometry restore failed", error: ex);
        }
    }

    /// <summary>Brings the open window forward, out of the Dock or taskbar if it is minimized.</summary>
    public void BringForward()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Localizer.Changed += OnLanguageChanged;
        RememberNormalGeometry();
        _vm.Start();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // The lifetime asks a window that has already closed again during a quit.
        if (e.Cancel || _closed)
            return;

        RememberNormalGeometry();
        if (WindowState is WindowState.Normal or WindowState.Maximized && _normalGeometry is { } normal)
        {
            _vm.SavePlacement(new WindowPlacement(
                normal.X, normal.Y, normal.Width, normal.Height,
                OperatingSystem.IsWindows() && WindowState == WindowState.Maximized));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        Localizer.Changed -= OnLanguageChanged;
        _vm.Dispose();
        base.OnClosed(e);
    }

    private void OnLanguageChanged() => _vm.Retranslate();

    private void ClampListToWindow() =>
        PaneGrid.ColumnDefinitions[0].Width = new GridLength(RecordsLayout.ListWidth(_listWidthIntent, ClientSize.Width));

    // Drag intent: window-conventions, Content-based minimum size. Only a finished drag saves.
    private void OnListSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _listWidthIntent = PaneGrid.ColumnDefinitions[0].ActualWidth;
        _vm.SaveListWidth(_listWidthIntent);
    }

    private void AttachListScroll()
    {
        if (_listScroll is not null)
            _listScroll.ScrollChanged -= OnListScrollChanged;
        _listScroll = RecordsList.Scroll as ScrollViewer;
        if (_listScroll is not null)
            _listScroll.ScrollChanged += OnListScrollChanged;
    }

    // Within about one screen of the end of what is loaded, the next page is read; at the top, new
    // records are shown as they arrive.
    private void OnListScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_listScroll is not { } scroll)
            return;

        var atTop = scroll.Offset.Y < 1;
        var nearEnd = scroll.Extent.Height - scroll.Offset.Y - scroll.Viewport.Height <= scroll.Viewport.Height;
        _vm.ViewportChanged(atTop, nearEnd, byReader: e.OffsetDelta.Y != 0);
    }

    private void RememberNormalGeometry()
    {
        if (_closed || WindowState != WindowState.Normal)
            return;

        // Avalonia reports macOS title-bar zoom as Normal; the zoomed frame is not the normal one.
        var screen = Screens.ScreenFromWindow(this);
        if (screen is not null
            && WindowMetrics.IsMaximizedGeometry(FrameSize ?? new Size(Width, Height), screen.WorkingArea, screen.Scaling))
        {
            return;
        }

        _normalGeometry = (Position.X, Position.Y, Width, Height);
    }

    private void RememberNormalGeometryAfterNativeEvents() => Dispatcher.UIThread.Post(RememberNormalGeometry);
}
