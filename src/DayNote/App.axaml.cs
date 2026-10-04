using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DayNote.I18n;
using DayNote.Services;
using DayNote.ViewModels;
using DayNote.Views;

namespace DayNote;

public partial class App : Application
{
    internal static Message? StartupFailureMessage { get; set; }

    /// <summary>
    /// The computer's own languages, in order, as <c>LanguageBootstrap</c> read them before the app was
    /// built. Handed to the view model so a language saved in Settings resolves System the same way.
    /// </summary>
    internal static IReadOnlyList<string> ComputerLanguages { get; set; } = [];

    // The main window and its view model, which the app menu's About and Settings items open through.
    // Null while a startup failure is shown instead, when those items are disabled.
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        MenuItemGestureSpace.Install();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The one macOS menu bar, set before any window so every window, a startup failure notice
            // included, shows the same bar. About and Settings are enabled while the main window is in
            // front, so never over one of its dialogs.
            MacMenuBar.Install(
                "DayNote",
                showAbout: () => _viewModel?.OpenAboutCommand.Execute(null),
                showSettings: () => _viewModel?.OpenSettingsCommand.Execute(null),
                canShowAppDialogs: () => _mainWindow is { IsActive: true });

            // An emoji chosen in the macOS picker arrives while the window is in the background; macOS only.
            BackgroundTextInput.Install();

            if (StartupFailureMessage is { } startupFailure)
            {
                desktop.MainWindow = MessageDialog.CreateStartupFailure(
                    Message.Of("startup.failedTitle"),
                    startupFailure);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // The view model owns its stores and gates all startup I/O (directory creation, reading
            // config/state) so a failure becomes an in-app error rather than a pre-UI crash.
            var dialogs = new DialogService(Program.Log);
            var viewModel = new MainWindowViewModel(Program.Paths, dialogs, Program.Log)
            {
                ComputerLanguages = ComputerLanguages,
            };
            _viewModel = viewModel;
            // Before the main window exists, so its first frame and title bar take the saved theme.
            // A startup failure above never reads settings, so its dialog follows the OS.
            AppTheme.Apply(viewModel.Theme);
            var window = new MainWindow
            {
                DataContext = viewModel,
                RecordsSource = Program.Records,
            };
            dialogs.Owner = window;

            window.RestoreWindowGeometry();

            desktop.MainWindow = window;
            _mainWindow = window;
            RegisterOwnerActivation(window);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // A second launch, and on macOS a click on the Dock icon, bring the main window back. AppKit
    // restores a minimized window on a Dock click only while no other window is visible, so with the
    // Records window open the main window would otherwise stay in the Dock.
    private void RegisterOwnerActivation(Window window)
    {
        void BringBack()
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            if (!window.IsVisible)
                window.Show();
            window.Activate();
        }

        SingleInstanceLease.RegisterOwnerActivationHandler(() => Dispatcher.UIThread.Post(BringBack));
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen)
                    BringBack();
            };
        }
    }
}
