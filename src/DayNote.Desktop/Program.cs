using Avalonia;
using DayNote.Core.Storage;
using Serilog;

namespace DayNote.Desktop;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = new AppPaths();
        try
        {
            paths.EnsureCreated();
        }
        catch
        {
            // Logging is configured next; directory creation is retried by the view model.
        }

        // One log file per session, named with a UTC timestamp and no app-name prefix. Serilog's
        // built-in retention only prunes within a rolling sequence of one path, so retention across
        // distinct per-session files is enforced here by keeping the newest files.
        PruneLogs(paths.LogsDirectory, keep: 30);
        var logFile = Path.Combine(paths.LogsDirectory, DayNote.Core.Time.DayNoteTime.FileStamp(DateTimeOffset.UtcNow) + ".log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logFile, rollingInterval: RollingInterval.Infinite, shared: true)
            .CreateLogger();

        try
        {
            Log.Information("DayNote starting");
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "DayNote terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.Information("DayNote shutting down");
            Log.CloseAndFlush();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void PruneLogs(string directory, int keep)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var logs = Directory.GetFiles(directory, "*.log")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Skip(keep);

            foreach (var path in logs)
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // A log file in use by another instance is left in place.
                }
            }
        }
        catch
        {
            // Pruning is best effort and must never prevent startup.
        }
    }
}
