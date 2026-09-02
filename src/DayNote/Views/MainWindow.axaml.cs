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

    // The pixel width the user last dragged each side pane to (the "intent"). Only a splitter drag
    // updates these; a window resize re-derives the displayed width but never overwrites the intent,
    // so growing the window back restores the pane to the user's chosen size.
    private double? _bindersWidthIntent;
    private double? _notesWidthIntent;
    private double? _attachmentsWidthIntent;

    // Avalonia owns the attachment drag session. A serializable application format gives macOS a real
    // pasteboard item; using an in-process-only object here previously crashed NSDraggingSession.
    private static readonly DataFormat<string> AttachmentReorderFormat =
        DataFormat.CreateStringApplicationFormat("com.nao7sep.daynote.attachment-reorder");

    private TaskCompletionSource<bool>? _attachDragIntent;
    private AttachmentItemViewModel? _attachDragItem;
    private Point? _attachDragOrigin;
    private IReadOnlyList<AttachmentItemViewModel>? _attachStartOrder;
    private bool _attachReordering;
    private string? _attachDragToken;

    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://DayNote/Assets/icon-win.png"));
            Icon = new WindowIcon(iconStream);
        }

        Loaded += OnLoaded;

        // The attachments pane accepts external file drops (add).
        AttachPane.AddHandler(DragDrop.DragOverEvent, OnAttachDragOver);
        AttachPane.AddHandler(DragDrop.DragLeaveEvent, OnAttachDragLeave);
        AttachPane.AddHandler(DragDrop.DropEvent, OnAttachDrop);

        // Keep only the click-versus-drag intention threshold here. Once it is crossed, Avalonia owns
        // pointer capture, cursor feedback, target routing, cancellation, and terminal cleanup.
        AttachList.AddHandler(PointerPressedEvent, OnAttachItemPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AttachList.AddHandler(PointerMovedEvent, OnAttachItemPointerMoved);
        AttachList.AddHandler(PointerReleasedEvent, OnAttachItemPointerReleased, handledEventsToo: true);
        AttachList.PointerCaptureLost += (_, _) => CancelAttachDragIntent();
        AttachList.DetachedFromVisualTree += (_, _) =>
        {
            CancelAttachDragIntent();
            ClearAttachDropHighlight();
        };
        AttachList.AddHandler(DragDrop.DragOverEvent, OnAttachReorderDragOver);
        AttachList.AddHandler(DragDrop.DropEvent, OnAttachReorderDrop);
        AttachList.AddHandler(KeyDownEvent, OnAttachListKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        Deactivated += (_, _) =>
        {
            CancelAttachDragIntent();
            ClearAttachDropHighlight();
        };
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

    private async void OnAttachItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Ignore presses on a button (the row's ✕) so they click rather than start a drag.
        if (e.Source is Visual v && (v is Button || v.GetVisualAncestors().OfType<Button>().Any()))
        {
            return;
        }

        if (_attachReordering || _attachDragIntent is not null
            || !e.GetCurrentPoint(AttachList).Properties.IsLeftButtonPressed
            || (e.Source as Control)?.DataContext is not AttachmentItemViewModel item)
        {
            return;
        }

        // The grabbed item is the active item for the whole transaction. Set this explicitly so a
        // drag that crosses the platform's selection threshold still follows stable identity.
        AttachList.SelectedItem = item;
        (AttachList.ContainerFromIndex(AttachList.Items.IndexOf(item)) as Control)?.Focus();
        _attachDragItem = item;
        _attachDragOrigin = e.GetPosition(AttachList);
        var intent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _attachDragIntent = intent;

        var intended = await intent.Task;
        if (!ReferenceEquals(_attachDragIntent, intent))
        {
            return;
        }

        _attachDragIntent = null;
        _attachDragOrigin = null;
        if (!intended || _attachDragItem is not { } activeItem)
        {
            _attachDragItem = null;
            return;
        }

        await RunAttachmentReorderAsync(e, activeItem);
    }

    private void OnAttachItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_attachDragIntent is not { } intent || _attachDragOrigin is not { } origin)
        {
            return;
        }

        if (!e.GetCurrentPoint(AttachList).Properties.IsLeftButtonPressed)
        {
            CancelAttachDragIntent();
            return;
        }

        if (AttachmentReorder.ExceedsDragThreshold(origin, e.GetPosition(AttachList)))
        {
            intent.TrySetResult(true);
        }
    }

    private void OnAttachItemPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        CancelAttachDragIntent();

    private async Task RunAttachmentReorderAsync(
        PointerPressedEventArgs trigger,
        AttachmentItemViewModel item)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Attachments.IndexOf(item) < 0)
        {
            _attachDragItem = null;
            return;
        }

        _attachReordering = true;
        _attachStartOrder = vm.Attachments.ToArray();
        _attachDragToken = Guid.NewGuid().ToString("N");
        using var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(AttachmentReorderFormat, _attachDragToken));

        var result = DragDropEffects.None;
        try
        {
            result = await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Move);
        }
        finally
        {
            FinishAttachDrag(commit: result == DragDropEffects.Move);
        }
    }

    private void OnAttachReorderDragOver(object? sender, DragEventArgs e)
    {
        PreviewAttachmentReorder(e);
    }

    private void OnAttachReorderDrop(object? sender, DragEventArgs e)
    {
        PreviewAttachmentReorder(e);
    }

    private void PreviewAttachmentReorder(DragEventArgs e)
    {
        var isCurrentReorder = _attachReordering
            && _attachDragToken is { } token
            && e.DataTransfer.TryGetValue(AttachmentReorderFormat) == token;
        if (!isCurrentReorder)
        {
            return; // external file delivery continues bubbling to AttachPane
        }

        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        if (DataContext is not MainWindowViewModel vm
            || _attachDragItem is not { } item
            || (e.Source as Control)?.DataContext is not AttachmentItemViewModel target)
        {
            return;
        }

        var targetIndex = vm.Attachments.IndexOf(target);
        if (targetIndex < 0)
        {
            return;
        }

        var keepFocus = AttachList.IsKeyboardFocusWithin;
        vm.MoveAttachment(item, targetIndex);
        FollowAttachment(item, keepFocus);
        e.DragEffects = DragDropEffects.Move;
    }

    private void OnAttachListKeyDown(object? sender, KeyEventArgs e)
    {
        if (_attachReordering || ComposingTextBox.IsFocusedElementComposing(this)
            || DataContext is not MainWindowViewModel vm
            || AttachList.SelectedItem is not AttachmentItemViewModel item)
        {
            return;
        }

        var offset = AttachmentReorder.KeyboardOffset(e.Key, e.KeyModifiers);
        var oldIndex = vm.Attachments.IndexOf(item);
        if (offset == 0 || !vm.MoveAttachment(item, oldIndex + offset))
        {
            return;
        }

        // Keyboard reorder is one complete transaction: the same move operation as pointer preview,
        // followed by one commit. The stable item remains selected and, because the list owns focus,
        // follows its new container without adding a tab stop or stealing focus from another control.
        vm.CommitAttachmentOrder();
        FollowAttachment(item, AttachList.IsKeyboardFocusWithin);
        e.Handled = true;
    }

    private void FinishAttachDrag(bool commit)
    {
        var activeItem = _attachDragItem;
        var keepFocus = AttachList.IsKeyboardFocusWithin;
        if (_attachReordering && DataContext is MainWindowViewModel vm)
        {
            if (commit)
            {
                vm.CommitAttachmentOrder();
            }
            else if (_attachStartOrder is { } startingOrder && !vm.RestoreAttachmentOrder(startingOrder))
            {
                // The list changed while captured (for example, a note reload). The snapshot no
                // longer describes this rendered list, so explicitly commit its current order rather
                // than applying stale item identities or leaving display and storage divergent.
                vm.CommitAttachmentOrder();
            }
        }

        ClearAttachDrag();
        if (activeItem is not null)
        {
            FollowAttachment(activeItem, keepFocus);
        }
    }

    private void FollowAttachment(AttachmentItemViewModel item, bool restoreFocus)
    {
        var index = AttachList.Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        AttachList.SelectedItem = item;
        AttachList.ScrollIntoView(item);
        AttachList.UpdateLayout();
        if (restoreFocus)
        {
            (AttachList.ContainerFromIndex(index) as Control)?.Focus();
        }
    }

    private void ClearAttachDrag()
    {
        _attachDragItem = null;
        _attachDragOrigin = null;
        _attachStartOrder = null;
        _attachDragToken = null;
        _attachReordering = false;
    }

    private void CancelAttachDragIntent()
    {
        var intent = _attachDragIntent;
        if (intent is null)
        {
            return;
        }

        _attachDragIntent = null;
        _attachDragItem = null;
        _attachDragOrigin = null;
        intent.TrySetResult(false);
    }

    private void ClearAttachDropHighlight()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsAttachmentDropActive = false;
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

        MinWidth = WindowMetrics.MinWidthFor(PaneGrid.ColumnDefinitions.Select(c => c.MinWidth));
        MinHeight = WindowMetrics.MinHeightFor(
            EditorPaneContentMinHeight(),
            ResultsViewport.MaxHeight + ResultsViewport.Margin.Top + ResultsViewport.Margin.Bottom);

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
