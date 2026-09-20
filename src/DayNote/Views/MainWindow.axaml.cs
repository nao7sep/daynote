using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Controls;
using DayNote.ViewModels;

namespace DayNote.Views;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;
    private IReadOnlyList<ShortcutItem>? _shortcuts;
    private (int X, int Y, double Width, double Height)? _normalGeometry;

    // The pixel width the user last dragged each side pane to (the "intent"). Only a splitter drag
    // updates these; a window resize re-derives the displayed width but never overwrites the intent,
    // so growing the window back restores the pane to the user's chosen size.
    private double? _bindersWidthIntent;
    private double? _notesWidthIntent;
    private double? _attachmentsWidthIntent;

    // Drag and Cmd/Ctrl+Shift+Up/Down reordering; Avalonia owns each drag session.
    private readonly ListReorder<BinderListItemViewModel> _binderReorder;
    private readonly ListReorder<AttachmentItemViewModel> _attachmentReorder;

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private MainWindowViewModel? _themeSource;

    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://DayNote/Assets/icon-win.png"));
            Icon = new WindowIcon(iconStream);
        }

        Loaded += OnLoaded;
        WindowViewport.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty)
                ClampPanesToWindow();
        };
        PositionChanged += (_, _) =>
        {
            RememberNormalGeometryAfterNativeEvents();
            ApplyNativeMinimum();
        };
        Resized += (_, _) => RememberNormalGeometryAfterNativeEvents();
        ScalingChanged += (_, _) => ApplyNativeMinimum();
        Screens.Changed += OnScreensChanged;
        Closed += (_, _) => Screens.Changed -= OnScreensChanged;

        // The attachments pane accepts external file drops (add).
        AttachPane.AddHandler(DragDrop.DragOverEvent, OnAttachDragOver);
        AttachPane.AddHandler(DragDrop.DragLeaveEvent, OnAttachDragLeave);
        AttachPane.AddHandler(DragDrop.DropEvent, OnAttachDrop);

        // Each list's reorder is one live move, one commit, and one restore on the view model; the
        // attachments list's reorder drags stay distinct from external file drops onto the pane.
        _binderReorder = new ListReorder<BinderListItemViewModel>(
            BindersList,
            (item, target) => Vm?.MoveBinder(item, target) ?? false,
            () => Vm?.CommitBinderOrder(),
            () => Vm?.BinderOrder() ?? [],
            order => Vm?.RestoreBinderOrder(order) ?? false);
        _attachmentReorder = new ListReorder<AttachmentItemViewModel>(
            AttachList,
            (item, target) => Vm is { } vm && vm.MoveAttachment(item, vm.Attachments.IndexOf(target)),
            () => Vm?.CommitAttachmentOrder(),
            () => Vm?.Attachments.ToArray() ?? [],
            order => Vm?.RestoreAttachmentOrder(order) ?? false);
        AttachList.DetachedFromVisualTree += (_, _) => ClearAttachDropHighlight();
        Deactivated += (_, _) =>
        {
            _binderReorder.CancelIntent();
            _attachmentReorder.CancelIntent();
            ClearAttachDropHighlight();
        };

        AttachmentsHeader.SizeChanged += (_, _) => KeepResultsBelowHeaders();
        EditorHeader.SizeChanged += (_, _) => KeepResultsBelowHeaders();
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
        ClearAttachDropHighlight();
    }

    private void OnAttachDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        ClearAttachDropHighlight();
        var deliveredItems = e.DataTransfer.TryGetFiles()?.ToArray() ?? [];
        var paths = deliveredItems
            .OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        if (deliveredItems.Length > 0)
        {
            vm.AddDroppedFiles(paths, deliveredItems.Length - paths.Count);
        }

        e.Handled = true;
    }

    private void ClearAttachDropHighlight()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsAttachmentDropActive = false;
        }
    }

    // The result stack floats beside the attachments header and the editor's title row. Its region
    // starts below both, so a stack of any height scrolls beneath them rather than covering their
    // controls. Only these headers' sizes move that line; the window's size does not.
    private void KeepResultsBelowHeaders()
    {
        var top = 0.0;
        foreach (var header in new Control[] { AttachmentsHeader, EditorHeader })
        {
            if (header.IsEffectivelyVisible
                && header.TranslatePoint(new Point(0, header.Bounds.Height), PaneTrack) is { } bottom)
            {
                top = Math.Max(top, bottom.Y);
            }
        }

        if (ResultsHost.Margin.Top != top)
        {
            ResultsHost.Margin = new Thickness(0, top, 0, 0);
        }
    }

    private void DismissResult_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm
            && sender is Control { DataContext: OperationResultViewModel result })
        {
            vm.DismissResult(result);
        }
    }

    private void DismissAttachmentResult_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.DismissAttachmentResult();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            _bindersWidthIntent = vm.BindersPaneWidth;
            _notesWidthIntent = vm.NotesPaneWidth;
            _attachmentsWidthIntent = vm.AttachmentsPaneWidth;
        }

        ApplyWindowMinimums();

        ClampPanesToWindow();

        PropertyChanged += OnWindowPropertyChanged;
        BindersSplitter.AddHandler(Thumb.DragCompletedEvent, OnBindersSplitterDragCompleted);
        NotesSplitter.AddHandler(Thumb.DragCompletedEvent, OnNotesSplitterDragCompleted);
        AttachmentsSplitter.AddHandler(Thumb.DragCompletedEvent, OnAttachmentsSplitterDragCompleted);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_themeSource is not null)
            _themeSource.PropertyChanged -= OnViewModelPropertyChanged;
        _themeSource = DataContext as MainWindowViewModel;
        if (_themeSource is not null)
            _themeSource.PropertyChanged += OnViewModelPropertyChanged;
    }

    // The theme is app-wide: when Settings commits a new one, every window and title bar follows.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.Theme) && sender is MainWindowViewModel vm)
            AppTheme.Apply(vm.Theme);
    }

    private double EditorPaneContentMinHeight() =>
        EditorPane.Child is Control content ? content.MinHeight : 0;

    private void ApplyWindowMinimums(Screen? target = null)
    {
        LayoutRoot.MinWidth = WindowMetrics.MinWidthFor(PaneGrid.ColumnDefinitions.Select(c => c.MinWidth));
        LayoutRoot.MinHeight = WindowMetrics.MinHeightFor(EditorPaneContentMinHeight());
        ApplyNativeMinimum(target);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ClientSizeProperty || e.Property == BoundsProperty)
        {
            ClampPanesToWindow();
        }
    }

    private void ClampPanesToWindow()
    {
        if (_bindersWidthIntent is not { } binders
            || _notesWidthIntent is not { } notes
            || _attachmentsWidthIntent is not { } attachments)
            return;

        var cols = PaneGrid.ColumnDefinitions;
        var budget = WindowMetrics.SidePaneBudget(Math.Max(WindowViewport.Viewport.Width, LayoutRoot.MinWidth), cols[4].MinWidth);
        var intents = new[] { binders, notes, attachments };
        var mins = new[] { cols[0].MinWidth, cols[2].MinWidth, cols[6].MinWidth };
        var displays = WindowMetrics.DistributeSidePanes(intents, mins, budget);

        cols[0].Width = new GridLength(displays[0], GridUnitType.Pixel);
        cols[2].Width = new GridLength(displays[1], GridUnitType.Pixel);
        cols[6].Width = new GridLength(displays[2], GridUnitType.Pixel);
        cols[4].Width = new GridLength(1, GridUnitType.Star);
    }

    private void OnBindersSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _bindersWidthIntent = PaneGrid.ColumnDefinitions[0].ActualWidth;
        _notesWidthIntent = PaneGrid.ColumnDefinitions[2].ActualWidth;
    }

    private void OnNotesSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _notesWidthIntent = PaneGrid.ColumnDefinitions[2].ActualWidth;
    }

    private void OnAttachmentsSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _attachmentsWidthIntent = PaneGrid.ColumnDefinitions[6].ActualWidth;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RememberNormalGeometry();
        if (DataContext is MainWindowViewModel vm)
        {
            vm.NoteCreated += OnNoteCreated;
            _ = vm.InitializeAsync();
        }
    }

    private void OnScreensChanged(object? sender, EventArgs e) => ApplyNativeMinimum();

    public void RestoreWindowGeometry()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        try
        {
            var target = Screens.All.FirstOrDefault(screen => WindowMetrics.CanRestoreWindowGeometry(
                vm.WindowPositionX, vm.WindowPositionY, vm.WindowWidth, vm.WindowHeight,
                [screen.WorkingArea]));
            if (target is null)
            {
                return;
            }

            ApplyWindowMinimums(target);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(vm.WindowPositionX!.Value, vm.WindowPositionY!.Value);
            Width = vm.WindowWidth!.Value;
            Height = vm.WindowHeight!.Value;
            _normalGeometry = (
                vm.WindowPositionX!.Value, vm.WindowPositionY!.Value,
                vm.WindowWidth!.Value, vm.WindowHeight!.Value);
            WindowState = WindowMetrics.RestoredWindowState(
                vm.WindowMaximized, OperatingSystem.IsWindows());
        }
        catch (Exception ex)
        {
            // Placement is disposable. Keep the designed defaults if the display backend or a
            // saved value cannot be used; startup and user data remain unaffected.
            Program.Log?.Warn("Window geometry restore failed", error: ex);
        }
    }

    private void ApplyNativeMinimum(Screen? target = null)
    {
        try
        {
            var floor = new Size(LayoutRoot.MinWidth, LayoutRoot.MinHeight);
            var screen = target ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
            var client = ClientSize;
            var frame = FrameSize ?? client;
            var minimum = screen is null ? floor : WindowMetrics.CapMinimumToWorkArea(
                floor, screen.WorkingArea, screen.Scaling,
                new Size(Math.Max(0, frame.Width - client.Width), Math.Max(0, frame.Height - client.Height)));
            MinWidth = minimum.Width;
            MinHeight = minimum.Height;
        }
        catch (Exception ex)
        {
            // Leave the current native minimum intact when the display backend
            // is unavailable; content still owns its full minimum and scrolling.
            Program.Log?.Warn("Window minimum work-area update failed", error: ex);
        }
    }

    // A freshly created note should be ready to type into: move focus to the title (Enter then jumps to
    // the body, per Title_Submitted). Posted so the editor pane is realized for the new selection first.
    private void OnNoteCreated(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => TitleBox.Focus(), DispatcherPriority.Background);

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_shutdownComplete && DataContext is MainWindowViewModel vm)
        {
            e.Cancel = true;
            CapturePaneWidths(vm);
            RememberNormalGeometry();
            if (WindowState is WindowState.Normal or WindowState.Maximized
                && _normalGeometry is { } normal)
            {
                vm.CaptureWindowPlacement(
                    normal.X, normal.Y, normal.Width, normal.Height,
                    OperatingSystem.IsWindows() && WindowState == WindowState.Maximized);
            }

            // Complete the quit only if the final flush succeeded. On failure ShutdownAsync keeps the
            // binder open with the autosave retrying, so the window stays open rather than discarding
            // unsaved edits on the way out.
            if (await vm.ShutdownAsync())
            {
                _shutdownComplete = true;
                Close();
            }

            return;
        }

        base.OnClosing(e);
    }

    private void RememberNormalGeometry()
    {
        if (WindowState != WindowState.Normal)
            return;

        // Avalonia reports macOS title-bar zoom as Normal. Judge the settled native frame too,
        // otherwise the zoomed rectangle replaces the actual normal rectangle.
        var screen = Screens.ScreenFromWindow(this);
        if (screen is not null
            && WindowMetrics.IsMaximizedGeometry(
                FrameSize ?? new Size(Width, Height), screen.WorkingArea, screen.Scaling))
        {
            return;
        }

        _normalGeometry = (Position.X, Position.Y, Width, Height);
    }

    private void RememberNormalGeometryAfterNativeEvents() =>
        Dispatcher.UIThread.Post(RememberNormalGeometry);

    // Symmetric with the OnOpened subscription, so the handler never outlives the window.
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.NoteCreated -= OnNoteCreated;
        }

        base.OnClosed(e);
    }

    private void CapturePaneWidths(MainWindowViewModel vm)
    {
        vm.BindersPaneWidth = _bindersWidthIntent ?? PaneGrid.ColumnDefinitions[0].ActualWidth;
        vm.NotesPaneWidth = _notesWidthIntent ?? PaneGrid.ColumnDefinitions[2].ActualWidth;
        vm.AttachmentsPaneWidth = _attachmentsWidthIntent ?? PaneGrid.ColumnDefinitions[6].ActualWidth;
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

    // Keyboard path to delete the selected note (the row ✕ is pointer-only). Delete, plus Back — the
    // physical delete key on a Mac keyboard. Scoped to the notes list, so Backspace in the editor body
    // still edits text rather than deleting the note.
    private void NotesList_KeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Delete || e.Key == Key.Back)
            && DataContext is MainWindowViewModel { SelectedNote: { } note } vm)
        {
            vm.DeleteNoteCommand.Execute(note);
            e.Handled = true;
        }
    }

    // The inline "✕" removes a binder from the list (closing it first if it's the open one).
    private void RemoveBinderRow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BinderListItemViewModel item } && DataContext is MainWindowViewModel vm)
        {
            vm.RemoveBinderCommand.Execute(item);
        }
    }

    // Double-tap a binder row to rename its title inline. The first tap of the double already selected
    // (and opened) the binder; this just enters edit mode and focuses the field.
    private void BinderRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: BinderListItemViewModel item } || item.IsEditing)
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
        if (sender is Control { DataContext: BinderListItemViewModel item } && DataContext is MainWindowViewModel vm)
        {
            vm.ApplyBinderRename(item, item.EditText);
        }
    }

    // Commit the inline binder rename. Submitted is raised by ComposingTextBox only on a genuine Enter —
    // an Enter consumed by the IME to accept a composition candidate arrives as Key.ImeProcessed and is
    // ignored — so renaming with an IME no longer commits (and tears the field closed) mid-composition.
    private void BinderTitle_Submitted(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: BinderListItemViewModel item } && DataContext is MainWindowViewModel vm)
        {
            vm.ApplyBinderRename(item, item.EditText);
        }
    }

    // Escape cancels the rename (Enter commits via Submitted above, which is the IME-safe path).
    private void BinderTitle_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is Control { DataContext: BinderListItemViewModel item })
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
        // A command accelerator is a chord the IME passes straight through, so while a field is
        // mid-composition the chord belongs to the pending candidate: stand down and let the user
        // finish, rather than firing on text the candidate is not yet part of (text-input-ime).
        if (ComposingTextBox.IsFocusedElementComposing(this))
        {
            return false;
        }

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

        // FilterNotes is handled here (it focuses a view control); every other action routes to
        // a view-model command through ShortcutRouter, whose completeness is asserted by a test.
        if (action == ShortcutAction.FilterNotes)
        {
            NotesFilterBox.Focus();
            return true;
        }

        var command = ShortcutRouter.CommandFor(vm, action);
        return command is not null && Run(command);
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
