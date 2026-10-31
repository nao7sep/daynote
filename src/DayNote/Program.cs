using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using DayNote.Core.Backup;
using DayNote.Core.Configuration;
using DayNote.Core.Storage;
using DayNote.I18n;
using DayNote.Logging;
using DayNote.Services;
using DayNote.ViewModels;
using DayNote.Views;

namespace DayNote;

internal static class Program
{
    /// <summary>The process-wide logger, available to the application once <see cref="Main"/> opens it.</summary>
    internal static IAppLogger Log { get; private set; } = null!;

    /// <summary>The records the Records window reads: the logger's own database.</summary>
    internal static IRecordsSource? Records { get; private set; }

    /// <summary>The single path resolver, resolved once here and threaded to the rest of the app.</summary>
    internal static AppPaths Paths { get; private set; } = null!;

    /// <summary>Why the records database was refused this session, or null while it is in use.</summary>
    internal static Exception? RecordsOpenError { get; private set; }

    /// <summary>
    /// The settings store and the outcome of its one startup load, read here for the language and handed
    /// to the main window, which owns presenting a load failure. Avalonia constructs the app itself, so
    /// startup values reach it through this class, as <see cref="Paths"/> and <see cref="Log"/> do.
    /// </summary>
    internal static (ConfigStore Store, AppConfig? Config, Exception? Error) Settings { get; private set; }

    // What remains of the exit's one deadline once the main window's quit has closed it, from when it did.
    // Writing what the quit logged, closing the records database and releasing the single-instance lease
    // draw on it, so DayNote never holds up a logout, restart or shutdown.
    private static TimeSpan s_exitLeft;
    private static long s_exitFixed;

    internal static void FixExitDeadline(TimeSpan remaining)
    {
        s_exitLeft = remaining;
        s_exitFixed = Stopwatch.GetTimestamp();
    }

    // An exit without the window's quit, such as a crash, fixes the deadline as it begins.
    private static TimeSpan ExitRemaining()
    {
        if (s_exitFixed == 0)
            FixExitDeadline(MainWindowViewModel.ExitBound);
        var remaining = s_exitLeft - Stopwatch.GetElapsedTime(s_exitFixed);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        AppPaths? resolved = null;
        Exception? unresolved = null;
        try
        {
            resolved = new AppPaths();
        }
        catch (Exception ex)
        {
            unresolved = ex;
        }

        // Language is needed before the logger can open. Keep validation warnings until that owner is up.
        var bootstrapWarnings = new List<string>();
        IAppLogger? startupLogger = null;
        var configStore = resolved is null ? null : new ConfigStore(resolved.ConfigFile, key =>
        {
            if (startupLogger is { } log)
            {
                log.Warn("Invalid configuration set; using built-in", new { key });
            }
            else
            {
                bootstrapWarnings.Add(key);
            }
        });
        AppConfig? config = null;
        Exception? configError = null;
        if (configStore is not null)
        {
            try
            {
                config = configStore.Load();
            }
            catch (Exception ex)
            {
                configError = ex;
            }

            Settings = (configStore, config, configError);
        }

        // The interface language, before anything can draw and before Avalonia creates the macOS
        // application object, which settles the language of the menu items AppKit contributes itself.
        // Settings that could not be loaded mean the computer's own languages, so the startup-failure
        // paths below have a usable interface language.
        App.ComputerLanguages = LanguageBootstrap.Start(config?.Language ?? Languages.System);

        if (resolved is not { } paths)
        {
            // An unusable DAYNOTE_DATA_DIR (or an unknown home) is a startup error we report and STOP on.
            // The logger isn't up yet — its directory derives from these very paths — so stderr is the
            // channel, and it stays English with the exception's own message: this is the diagnostic
            // channel, not an interface surface. What the reader sees is the notice window.
            Console.Error.WriteLine(
                "DayNote cannot start: its storage location could not be resolved. " + unresolved!.Message);
            App.StartupFailureMessage = FailurePresentation.StartupStorage();
            _ = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 1;
        }

        try
        {
            paths.EnsureCreated();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                "DayNote cannot start: its storage location could not be created. " + ex.Message);
            App.StartupFailureMessage = FailurePresentation.StartupStorage();
            _ = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 1;
        }

        SingleInstanceLease? instanceLease;
        try
        {
            if (!SingleInstanceLease.TryAcquire(paths.Root, out instanceLease))
            {
                Console.Error.WriteLine("DayNote is already running; activated the existing window.");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("DayNote cannot start: its storage location could not be reserved. " + ex.Message);
            App.StartupFailureMessage = FailurePresentation.StartupStorage();
            _ = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 1;
        }

        // Once the storage root is owned, the logger is the first diagnostic subsystem up and can
        // record every later failure.
        var logger = RecordsLogger.Open(paths.RecordsFile, paths.LogsDirectory, DebugEnabled());
        Log = logger;
        startupLogger = logger;
        foreach (var key in bootstrapWarnings)
        {
            logger.Warn("Invalid configuration set; using built-in", new { key });
        }
        bootstrapWarnings.Clear();
        Records = logger;
        RecordsOpenError = logger.OpenError;
        Paths = paths;
        RegisterCrashHooks(logger);

        // The backup history (data-backup-conventions) lives in DayNote.Core and must
        // stay logger-framework-free, so it takes its one edge concern — a warn on a record/open failure —
        // as a delegate installed here. Installed before any managed save so the very first record's
        // failure (should one occur) is logged. The store logs ONLY failures; success is silent.
        BackupStore.ConfigureWarn((message, path, error) => logger.Warn(message, new { file = path }, error));
        AppPaths.ConfigureWarn((message, error) => logger.Warn(message, new { root = paths.Root }, error));

        logger.Info("DayNote starting", new
        {
            version = AppInfo.Version,
            runtime = RuntimeInformation.FrameworkDescription,
            language = Localizer.Language,
        });

        var forced = false;
        try
        {
            // On macOS a logout or shutdown ends the process as soon as the lifetime exits, before the
            // close below runs, so what the quit logged is written here first, within the exit's deadline.
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                args,
                lifetime => lifetime.Exit += (_, _) => logger.Flush(ExitRemaining()));
        }
        catch (Exception ex)
        {
            forced = true;
            logger.Error("DayNote terminated unexpectedly", error: ex);
            return 1;
        }
        finally
        {
            logger.Info("DayNote shutting down", new { reason = forced ? "forced" : "clean" });
            logger.Close(ExitRemaining());
            instanceLease!.Dispose(ExitRemaining());
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(MacMenuBar.PlatformOptions())
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Last-resort hooks: an exception escaping a background thread, or a faulted task whose result
    /// is never observed, is logged at <c>error</c>, and a terminating one is written before the
    /// process dies.
    /// </summary>
    private static void RegisterCrashHooks(RecordsLogger log)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Error("Unhandled exception", new { terminating = e.IsTerminating }, e.ExceptionObject as Exception);
            if (e.IsTerminating)
            {
                log.Flush(ExitRemaining());
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error("Unobserved task exception", error: e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Debug logging is for developers only: on from an unpackaged/development build, or when
    /// <c>DAYNOTE_DEBUG=1</c> is set; off in release builds so it never floods an end user's disk.
    /// </summary>
    private static bool DebugEnabled()
    {
#if DEBUG
        return true;
#else
        return Environment.GetEnvironmentVariable("DAYNOTE_DEBUG") == "1";
#endif
    }
}
