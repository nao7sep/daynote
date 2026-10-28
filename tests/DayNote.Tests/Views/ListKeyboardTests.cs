using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using DayNote.Tests.I18n;
using DayNote.Tests.Storage;
using DayNote.ViewModels;
using DayNote.Views;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The keyboard paths for the list rows' pointer gestures: the platform's rename key renames the
/// selected binder, as a double-click does, and Enter opens the selected attachment.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class ListKeyboardTests : WindowTest
{
    [AvaloniaFact]
    public async Task The_rename_key_renames_the_selected_binder_and_hands_the_keyboard_back()
    {
        using var populated = PopulatedMainWindow.Create();
        var window = Show(populated.Window);
        await populated.FillAsync();
        var list = window.FindControl<ListBox>("BindersList")!;
        var item = Assert.IsType<BinderListItemViewModel>(list.SelectedItem);
        FocusRow(list);

        Press(window, RenameKey(window));

        Assert.True(item.IsEditing);
        var field = Assert.IsAssignableFrom<TextBox>(window.FocusManager!.GetFocusedElement());
        Assert.Equal(item.Title, field.Text);
        Dispatcher.UIThread.RunJobs();
        // The whole title is selected, so typing replaces it.
        Assert.Equal((0, item.Title.Length), (System.Math.Min(field.SelectionStart, field.SelectionEnd), System.Math.Max(field.SelectionStart, field.SelectionEnd)));

        // Enter submits from the field; on macOS it is also the rename key, and the list leaves it alone.
        field.Text = "Renamed";
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.Title)) saved.TrySetResult(); };
        Press(window, PhysicalKey.Enter);
        await saved.Task.WaitAsync(TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        Assert.False(item.IsEditing);
        var renamed = Assert.IsType<BinderListItemViewModel>(list.SelectedItem);
        Assert.Equal("Renamed", renamed.Title);
        Assert.False(renamed.IsEditing);
        Assert.Same(list.ContainerFromItem(renamed), window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task Escape_cancels_a_keyboard_rename_and_hands_the_keyboard_back()
    {
        using var populated = PopulatedMainWindow.Create();
        var window = Show(populated.Window);
        await populated.FillAsync();
        var list = window.FindControl<ListBox>("BindersList")!;
        var item = Assert.IsType<BinderListItemViewModel>(list.SelectedItem);
        var title = item.Title;
        FocusRow(list);

        Press(window, RenameKey(window));
        Assert.IsAssignableFrom<TextBox>(window.FocusManager!.GetFocusedElement()).Text = "Not kept";
        Press(window, PhysicalKey.Escape);

        Assert.False(item.IsEditing);
        Assert.Equal(title, item.Title);
        Assert.Same(list.ContainerFromItem(item), window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task Enter_opens_the_selected_attachment()
    {
        using var populated = PopulatedMainWindow.Create();
        var window = Show(populated.Window);
        await populated.FillAsync();
        var list = window.FindControl<ListBox>("AttachList")!;
        var attachment = populated.ViewModel.Attachments.First(row => row.Exists);
        list.SelectedItem = attachment;
        FocusRow(list);

        Press(window, PhysicalKey.Enter);

        await populated.ViewModel.OpenAttachmentCommand.ExecutionTask!;
        Assert.Equal(attachment.FullPath, populated.Dialogs.LastOpenedPath);
    }

    private static PhysicalKey RenameKey(TopLevel top) =>
        ShortcutCatalog.RenameKey(top) == Key.Enter ? PhysicalKey.Enter : PhysicalKey.F2;

    private static void FocusRow(ListBox list)
    {
        Assert.True(list.ContainerFromItem(list.SelectedItem!)!.Focus(NavigationMethod.Directional));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(TopLevel root, PhysicalKey key)
    {
        root.KeyPressQwerty(key, RawInputModifiers.None);
        root.KeyReleaseQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
}
