using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DayNote.Services;
using DayNote.ViewModels;
using DayNote.Views;

namespace DayNote;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The view model owns its stores and gates all startup I/O (directory creation, reading
            // config/state) so a failure becomes an in-app error rather than a pre-UI crash.
            var dialogs = new DialogService(Program.Log);
            var viewModel = new MainWindowViewModel(Program.Paths, dialogs, Program.Log);
            var window = new MainWindow { DataContext = viewModel };
            dialogs.Owner = window;

            desktop.MainWindow = window;

            // Report any quarantine the startup loads performed: the store was
            // set aside with its bytes preserved and defaults took over — the
            // user hears it from a dialog, never only from the log
            // (storage-path conventions: both branches report).
            window.Opened += async (_, _) =>
            {
                var quarantined = DayNote.Core.Storage.QuarantineJournal.Drain();
                if (quarantined.Count > 0)
                {
                    await dialogs.ShowErrorAsync(
                        "A settings file was reset",
                        "A file was unreadable and has been set aside so nothing is lost:\n\n" +
                        string.Join("\n", quarantined) +
                        "\n\nDayNote started with defaults for it. Your binders and notes are untouched.");
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
