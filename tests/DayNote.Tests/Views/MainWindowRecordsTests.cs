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

    [AvaloniaFact]
    public void Quitting_closes_the_records_window_first_and_keeps_its_placement()
    {
        using var populated = PopulatedMainWindow.Empty();
        var main = Show(populated.Window);
        main.RecordsSource = new FakeRecordsSource();
        main.OpenRecordsWindow();
        var records = main.RecordsWindow!;
        Dispatcher.UIThread.RunJobs();

        var closed = false;
        main.Closed += (_, _) => closed = true;
        main.Close();
        MainWindowQuitTests.PumpUntil(() => closed);

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
        var closed = false;
        main.Closed += (_, _) => closed = true;

        main.Close();
        main.OpenRecordsWindow();

        Assert.Null(main.RecordsWindow);
        MainWindowQuitTests.PumpUntil(() => closed);
    }
}
