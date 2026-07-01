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

            // Kick off the just-in-case data backup on a background thread so the window never waits on it.
            BackupService.RunInBackground(Program.Paths, Program.Log);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
