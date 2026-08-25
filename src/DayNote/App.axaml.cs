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
                        "A settings file was reset",
                        "A file was unreadable and has been set aside so nothing is lost:\n\n" +
                        string.Join("\n", quarantined) +
                        (lostBinderList
                            ? "\n\nDayNote started with an empty binder list. Your binder FILES are untouched on disk, "
                              + "but the list of which binders you had open — and any titles you set for them — is only "
                              + "in the file above. Re-open your binders, or recover the list from it, before quitting: "
                              + "quitting writes the empty list back."
                            : "\n\nDayNote started with defaults for it. Your binders and notes are untouched."));
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

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
