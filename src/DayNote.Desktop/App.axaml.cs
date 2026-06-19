using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DayNote.Core.Storage;
using DayNote.Desktop.Services;
using DayNote.Desktop.ViewModels;
using DayNote.Desktop.Views;

namespace DayNote.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The view model owns its stores and gates all startup I/O (directory creation, reading
            // config/state) so a failure becomes an in-app error rather than a pre-UI crash.
            var dialogs = new DialogService();
            var viewModel = new MainWindowViewModel(new AppPaths(), dialogs, Program.Log);
            var window = new MainWindow { DataContext = viewModel };
            dialogs.Owner = window;

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
