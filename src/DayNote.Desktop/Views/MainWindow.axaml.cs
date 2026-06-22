using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DayNote.Desktop.ViewModels;

namespace DayNote.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;
    private IReadOnlyList<ShortcutItem>? _shortcuts;

    // The pixel width the user last dragged each side pane to (the "intent"). Only a splitter drag
    // updates these; a window resize re-derives the displayed width but never overwrites the intent,
    // so growing the window back restores the pane to the user's chosen size.
    private double? _bindersWidthIntent;
    private double? _notesWidthIntent;
    private double? _attachmentsWidthIntent;

    // Attachment reorder is done with manual pointer capture, NOT OS drag-and-drop: initiating a native
    // drag with an in-app-only payload crashes the macOS backend (NSDraggingSession needs a pasteboard
    // item). External file drops below still use OS DnD, since the OS supplies that drag session.
    private AttachmentItemViewModel? _attachDragItem;
    private Point? _attachDragOrigin;
    private bool _attachReordering;
    private Control? _attachDragContainer;
    private int _attachStartIndex;
    private double _attachRowStep;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // The attachments pane accepts external file drops (add).
        AttachPane.AddHandler(DragDrop.DragOverEvent, OnAttachDragOver);
        AttachPane.AddHandler(DragDrop.DragLeaveEvent, OnAttachDragLeave);
        AttachPane.AddHandler(DragDrop.DropEvent, OnAttachDrop);

        // Press-and-drag a row to reorder it. handledEventsToo so the press is seen even if the ListBox
        // consumed it for selection; the press is ignored when it lands on a button (the ✕).
        AttachList.AddHandler(PointerPressedEvent, OnAttachItemPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AttachList.AddHandler(PointerMovedEvent, OnAttachItemPointerMoved);
        AttachList.AddHandler(PointerReleasedEvent, OnAttachItemPointerReleased, handledEventsToo: true);
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

    private void OnAttachItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Ignore presses on a button (the row's ✕) so they click rather than start a drag.
        if (e.Source is Visual v && (v is Button || v.GetVisualAncestors().OfType<Button>().Any()))
        {
            return;
        }

        if (e.GetCurrentPoint(AttachList).Properties.IsLeftButtonPressed
            && (e.Source as Control)?.DataContext is AttachmentItemViewModel item)
        {
            _attachDragItem = item;
            _attachDragOrigin = e.GetPosition(AttachList);
        }
    }

    private void OnAttachItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_attachDragItem is not { } item || _attachDragOrigin is not { } origin
            || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (!e.GetCurrentPoint(AttachList).Properties.IsLeftButtonPressed)
        {
            ClearAttachDrag();
            return;
        }

        var pos = e.GetPosition(AttachList);
        if (!_attachReordering)
        {
            if (Math.Abs(pos.X - origin.X) < 3 && Math.Abs(pos.Y - origin.Y) < 3)
            {
                return; // tiny threshold so a plain click is not treated as a drag
            }

            _attachReordering = true;
            _attachStartIndex = vm.Attachments.IndexOf(item);
            _attachDragContainer = AttachList.ContainerFromIndex(_attachStartIndex) as Control;
            _attachRowStep = MeasureRowStep();
            if (_attachDragContainer is not null)
            {
                _attachDragContainer.ZIndex = 1000; // float the grabbed row above its siblings
            }

            e.Pointer.Capture(AttachList); // keep receiving moves even if the pointer leaves a row
        }

        if (_attachDragContainer is null)
        {
            return;
        }

        var delta = pos.Y - origin.Y;
        if (_attachRowStep > 0)
        {
            // Reflow: shift the other rows to open a gap where the dragged row will land. The target
            // slot is the start index moved by however many whole rows the cursor has travelled.
            var target = Math.Clamp(
                _attachStartIndex + (int)Math.Round(delta / _attachRowStep, MidpointRounding.AwayFromZero),
                0,
                vm.Attachments.Count - 1);
            var current = vm.Attachments.IndexOf(item);
            if (target != current)
            {
                vm.MoveAttachment(item, target);
                AttachList.UpdateLayout(); // settle the reflow so the transform below stays glued to the cursor
                current = target;
            }

            // Keep the dragged row under the cursor by cancelling the layout shift its reorder caused.
            _attachDragContainer.RenderTransform =
                new TranslateTransform(0, delta - ((current - _attachStartIndex) * _attachRowStep));
        }
        else
        {
            _attachDragContainer.RenderTransform = new TranslateTransform(0, delta);
        }

        e.Handled = true;
    }

    private void OnAttachItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // The collection was reordered live during the drag; just persist the final order.
        if (_attachReordering && DataContext is MainWindowViewModel vm)
        {
            vm.CommitAttachmentOrder();
            e.Pointer.Capture(null);
        }

        ClearAttachDrag();
    }

    private void ClearAttachDrag()
    {
        if (_attachDragContainer is not null)
        {
            _attachDragContainer.RenderTransform = null;
            _attachDragContainer.ZIndex = 0;
            _attachDragContainer = null;
        }

        _attachDragItem = null;
        _attachDragOrigin = null;
        _attachReordering = false;
    }

    // The vertical distance between consecutive attachment rows (they are uniform), measured from the
    // first two containers so any inter-row spacing is included; falls back to the dragged row's height.
    private double MeasureRowStep()
    {
        if (AttachList.ItemCount > 1
            && AttachList.ContainerFromIndex(0) is Control a
            && AttachList.ContainerFromIndex(1) is Control b
            && a.TranslatePoint(default, AttachList) is { } pa
            && b.TranslatePoint(default, AttachList) is { } pb
            && Math.Abs(pb.Y - pa.Y) > 0)
        {
            return Math.Abs(pb.Y - pa.Y);
        }

        return _attachDragContainer?.Bounds.Height ?? 0;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            _bindersWidthIntent = vm.BindersPaneWidth;
            _notesWidthIntent = vm.NotesPaneWidth;
            _attachmentsWidthIntent = vm.AttachmentsPaneWidth;
        }

        MinWidth = WindowMetrics.MinWidthFor(PaneGrid.ColumnDefinitions.Select(c => c.MinWidth));
        MinHeight = WindowMetrics.MinHeightFor(EditorPaneContentMinHeight());

        ClampPanesToWindow();

        PropertyChanged += OnWindowPropertyChanged;
        BindersSplitter.AddHandler(Thumb.DragCompletedEvent, OnBindersSplitterDragCompleted);
        NotesSplitter.AddHandler(Thumb.DragCompletedEvent, OnNotesSplitterDragCompleted);
        AttachmentsSplitter.AddHandler(Thumb.DragCompletedEvent, OnAttachmentsSplitterDragCompleted);
    }

    private double EditorPaneContentMinHeight() =>
        EditorPane.Child is Control content ? content.MinHeight : 0;

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ClientSizeProperty || e.Property == BoundsProperty)
            ClampPanesToWindow();
    }

    private void ClampPanesToWindow()
    {
        if (_bindersWidthIntent is not { } binders
            || _notesWidthIntent is not { } notes
            || _attachmentsWidthIntent is not { } attachments)
            return;

        var cols = PaneGrid.ColumnDefinitions;
        var budget = WindowMetrics.SidePaneBudget(Width, cols[4].MinWidth);
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
        if (DataContext is MainWindowViewModel vm)
        {
            vm.NoteCreated += OnNoteCreated;
            _ = vm.InitializeAsync();
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
