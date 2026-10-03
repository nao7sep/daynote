using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DayNote.Core.Models;
using DayNote.Tests.I18n;
using DayNote.Tests.Storage;
using Xunit;

namespace DayNote.Tests.Views;

/// <summary>
/// The main window's quit is a command that cannot start again while it runs: a second request
/// while the first is still saving waits for it rather than starting a second shutdown.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class MainWindowQuitTests : WindowTest
{
    [AvaloniaFact]
    public async Task A_second_quit_while_the_first_saves_starts_no_second_shutdown()
    {
        var log = new RecordingLogger();
        using var populated = PopulatedMainWindow.Create(log);
        var main = Show(populated.Window);
        await populated.FillAsync();
        Dispatcher.UIThread.RunJobs();

        // An edit not yet saved, so the quit's final save runs off the UI thread.
        populated.ViewModel.Editor.Status = NoteStatus.Draft;
        populated.ViewModel.Editor.Body = "One more line before quitting.";
        var closed = false;
        main.Closed += (_, _) => closed = true;

        main.Close();
        Assert.False(closed);
        main.Close();
        PumpUntil(() => closed);

        Assert.Single(log.Entries, entry => entry.Message == "Application shutting down");
    }

    internal static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(1);
        }

        Assert.True(done(), "timed out");
    }
}
