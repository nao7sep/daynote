using DayNote.Core.Backup;
using DayNote.Core.Storage;
using DayNote.Logging;

namespace DayNote.Services;

/// <summary>
/// Kicks off the just-in-case data backup at startup, off the UI thread, and logs its outcome. This is the
/// edge that owns threading and logging; the pass itself is <see cref="BackupEngine"/> in DayNote.Core,
/// which does not log. Best-effort: it never blocks the window, shows an error, or crashes the app.
/// </summary>
public static class BackupService
{
    /// <summary>Runs one backup pass on a background thread and logs the report. Fire-and-forget.</summary>
    public static void RunInBackground(AppPaths paths, IAppLogger log)
    {
        _ = Task.Run(() =>
        {
            try
            {
                LogReport(new BackupEngine(paths).Run(DateTimeOffset.UtcNow), log);
            }
            catch (Exception ex)
            {
                // The engine already captures its own failures in the report; this is the final backstop so
                // a bug here can never surface to the user or take down the app.
                log.Error("backup: unexpected failure", error: ex);
            }
        });
    }

    private static void LogReport(BackupReport report, IAppLogger log)
    {
        foreach (var skip in report.Skips)
        {
            log.Warn("backup: skipped a file", new { path = skip.Path, reason = skip.Reason });
        }

        if (report.IndexWasReset)
        {
            log.Warn("backup: index was unreadable and reset; this run is a full backup");
        }

        if (report.Fatal is not null)
        {
            log.Error("backup: run failed", error: report.Fatal);
            return;
        }

        if (report.NothingChanged)
        {
            log.Debug("backup: nothing changed, no archive written");
            return;
        }

        log.Info("backup: archive written",
            new { archive = report.ArchiveFileName, files = report.FilesArchived });
    }
}
