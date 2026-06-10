using System.Runtime.InteropServices;
using Avalonia;
using DayNote.Core.Storage;
using DayNote.Desktop.Logging;

namespace DayNote.Desktop;

internal static class Program
{
    /// <summary>The process-wide logger, available to the application once <see cref="Main"/> opens it.</summary>
    internal static IAppLogger Log { get; private set; } = null!;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = new AppPaths();

        // One log file per launch, named with a UTC timestamp; the logger creates the logs directory
        // itself, so it is the first thing up and can record every later failure.
        var logger = JsonLinesLogger.Open(paths.LogsDirectory, DebugEnabled());
        Log = logger;
        RegisterCrashHooks(logger);

        try
        {
            paths.EnsureCreated();
        }
        catch (Exception ex)
        {
            // The logs directory already exists (the logger made it); the rest of the data directory
            // is retried by the view model, which turns a failure into an in-app error rather than a
            // pre-UI crash. Record it so the retry is not the first sign anything went wrong.
            logger.Warn("Could not pre-create the data directory", new { root = paths.Root }, ex);
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
