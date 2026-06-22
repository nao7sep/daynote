using System.Runtime.InteropServices;
using Avalonia;
using DayNote.Core.Storage;
using DayNote.Desktop.Logging;

namespace DayNote.Desktop;

internal static class Program
{
    /// <summary>The process-wide logger, available to the application once <see cref="Main"/> opens it.</summary>
    internal static IAppLogger Log { get; private set; } = null!;

    /// <summary>The single path resolver, resolved once here and threaded to the rest of the app.</summary>
    internal static AppPaths Paths { get; private set; } = null!;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        AppPaths paths;
        try
        {
            paths = new AppPaths();
        }
        catch (Exception ex)
        {
            // An unusable DAYNOTE_HOME (or an unknown home) is a startup error we report and STOP on.
            // The logger isn't up yet — its directory derives from these very paths — so stderr is the
            // channel; exit non-zero before any UI loads.
            Console.Error.WriteLine(
                "DayNote cannot start: its storage location could not be resolved. " + ex.Message);
            return 1;
        }

        // One log file per launch, named with a UTC timestamp; the logger creates the logs directory
        // itself, so it is the first thing up and can record every later failure.
        var logger = JsonLinesLogger.Open(paths.LogsDirectory, DebugEnabled());
        Log = logger;
        Paths = paths;
        RegisterCrashHooks(logger);

        try
        {
            paths.EnsureCreated();
        }
        catch (Exception ex)
        {
            // An unusable DAYNOTE_HOME (or an unwritable home) is a startup error we report and STOP
            // on — never a silent fallback that lets the app run unable to persist. The logger is
            // already up, so record it there and on stderr, then exit non-zero before any UI loads.
            logger.Error("DayNote cannot start: its storage location could not be created",
                new { root = paths.Root }, ex);
            Console.Error.WriteLine(
                "DayNote cannot start: its storage location could not be created. " + ex.Message);
            logger.Dispose();
            return 1;
        }

        logger.Info("DayNote starting", new
        {
            version = AppInfo.Version,
            runtime = RuntimeInformation.FrameworkDescription,
        });

        var forced = false;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
            logger.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Last-resort hooks: an exception escaping a background thread, or a faulted task whose result
    /// is never observed, is logged at <c>error</c> (which flushes immediately) before the process dies.
    /// </summary>
    private static void RegisterCrashHooks(IAppLogger log)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Error("Unhandled exception", new { terminating = e.IsTerminating }, e.ExceptionObject as Exception);

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
