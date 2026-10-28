using System.IO;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DayNote.Core.Storage;
using DayNote.Tests.I18n;
using DayNote.Tests.Storage;
using DayNote.Tests.ViewModels;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// How the main window holds the Records window: one at most, brought forward when opened again,
/// and closed before a quit writes state, so its placement is kept and it never keeps the app
/// running.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class MainWindowRecordsTests : WindowTest
{
    [AvaloniaFact]
    public void Opening_records_again_brings_the_one_window_forward()
    {
        using var populated = PopulatedMainWindow.Empty();
        var main = Show(populated.Window);
        main.RecordsSource = new FakeRecordsSource();

        main.OpenRecordsWindow();
        var records = Track(main.RecordsWindow!);
        Dispatcher.UIThread.RunJobs();
        main.OpenRecordsWindow();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(records, main.RecordsWindow);
        Assert.True(records.IsVisible);
    }

    // A window the user opens takes focus above the one it came from, and opening it again while it
    // sits behind or minimized brings it back with focus (window-conventions, activation).
    [AvaloniaFact]
    public void The_records_window_takes_focus_when_opened_and_when_opened_again()
    {
        using var populated = PopulatedMainWindow.Empty();
        var main = Show(populated.Window);
        main.RecordsSource = new FakeRecordsSource();

        main.OpenRecordsWindow();
        var records = Track(main.RecordsWindow!);
        Dispatcher.UIThread.RunJobs();
        // The headless platform never takes activation away from a window, so what is checked is that
        // the records window is activated each time, not that the main window loses it.
        Assert.True(records.IsActive);

        var activations = 0;
        records.Activated += (_, _) => activations++;
        main.Activate();
        records.WindowState = Avalonia.Controls.WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        main.OpenRecordsWindow();
        Dispatcher.UIThread.RunJobs();
        Assert.True(activations > 0);
        Assert.Equal(Avalonia.Controls.WindowState.Normal, records.WindowState);
    }

    [AvaloniaFact]
    public void Quitting_closes_the_records_window_first_and_keeps_its_placement()
    {
        using var populated = PopulatedMainWindow.Empty();
        var main = Show(populated.Window);
        main.RecordsSource = new FakeRecordsSource();
        main.OpenRecordsWindow();
        var records = main.RecordsWindow!;
        Dispatcher.UIThread.RunJobs();

        var closed = Closed(main);
        main.Close();
        RunUntil(closed);

        Assert.False(records.IsVisible);
        Assert.Null(main.RecordsWindow);
        using var state = JsonDocument.Parse(File.ReadAllText(new AppPaths().StateFile));
        Assert.True(state.RootElement.TryGetProperty("recordsWindowPositionX", out var x));
        Assert.Equal(JsonValueKind.Number, x.ValueKind);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task No_records_window_opens_while_the_app_quits()
    {
        using var populated = PopulatedMainWindow.Create();
        var main = Show(populated.Window);
        main.RecordsSource = new FakeRecordsSource();
        await populated.FillAsync();
        populated.ViewModel.Editor.Status = DayNote.Core.Models.NoteStatus.Draft;
        populated.ViewModel.Editor.Body = "Saved on the way out.";
        var closed = Closed(main);

        main.Close();
        main.OpenRecordsWindow();

        Assert.Null(main.RecordsWindow);
        RunUntil(closed);
    }
}
