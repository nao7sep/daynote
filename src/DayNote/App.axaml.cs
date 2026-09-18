using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DayNote.Services;
using DayNote.ViewModels;
using DayNote.Views;

namespace DayNote;

public partial class App : Application
{
    internal static string? StartupFailureMessage { get; set; }

    // The main window's view model, which the app menu's About and Settings items open through.
    // Null while a startup failure is shown instead, when those items do nothing.
    private MainWindowViewModel? _viewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (StartupFailureMessage is { } startupFailure)
            {
                desktop.MainWindow = new MessageDialog(
                    "DayNote could not start",
                    startupFailure,
                    [new DialogButton("Close", "close", DialogButtonKind.Primary)]);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // The view model owns its stores and gates all startup I/O (directory creation, reading
            // config/state) so a failure becomes an in-app error rather than a pre-UI crash.
            var dialogs = new DialogService(Program.Log);
            var viewModel = new MainWindowViewModel(Program.Paths, dialogs, Program.Log);
            _viewModel = viewModel;
            // Before the main window exists, so its first frame and title bar take the saved theme.
            // A startup failure above never reads settings, so its dialog follows the OS.
            AppTheme.Apply(viewModel.Theme);
            var window = new MainWindow
            {
                DataContext = viewModel,
            };
            dialogs.Owner = window;

            window.RestoreWindowGeometry();

            desktop.MainWindow = window;
            RegisterOwnerActivation(window);

            // Report material recovery once the main window can own the dialog.
            window.Opened += async (_, _) =>
            {
                var quarantined = DayNote.Core.Storage.QuarantineJournal.Drain();
                if (quarantined.Count > 0)
                {
                    // state.json contains the binder registry and needs more specific recovery copy.
                    var lostBinderList = quarantined.Any(
                        path => System.IO.Path.GetFileName(path).StartsWith("state-", StringComparison.Ordinal));
                    await dialogs.ShowErrorAsync(
                        lostBinderList ? "The binder list was reset" : "A settings file was reset",
                        FailurePresentation.RecoveredData(lostBinderList));
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void AboutMenu_Click(object? sender, EventArgs e) => _viewModel?.OpenAboutCommand.Execute(null);

    private void SettingsMenu_Click(object? sender, EventArgs e) => _viewModel?.OpenSettingsCommand.Execute(null);

    private static void RegisterOwnerActivation(Window window)
    {
        SingleInstanceLease.RegisterOwnerActivationHandler(() => Dispatcher.UIThread.Post(() =>
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            if (!window.IsVisible)
                window.Show();
            window.Activate();
        }));
    }
}
