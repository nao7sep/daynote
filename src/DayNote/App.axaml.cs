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
                    // Per store, because the two say opposite things to the user: config.json
                    // holds preferences that are simply re-authored, while state.json holds the
                    // known-binder paths AND their locally-stored titles — telling someone their
                    // binders are untouched while that list is empty is worse than saying nothing,
                    // since the .invalid copy is the only surviving record (storage-path
                    // conventions: the report names what was lost).
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
}
