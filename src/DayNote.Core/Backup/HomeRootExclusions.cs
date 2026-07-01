namespace DayNote.Core.Backup;

/// <summary>
/// The optimistic exclude list for the <c>~/.daynote/</c> home root: everything under the root is backed
/// up except the entries here. Pure so the "did we pick the right files?" decision is unit-testable.
/// Durable data (<c>config.json</c> and any future durable file) is captured; only genuinely throwaway or
/// self-managed paths are dropped. Paths are the forward-slash relative path under the root.
/// </summary>
public static class HomeRootExclusions
{
    /// <summary>
    /// True when a home-root file must not be backed up:
    /// <list type="bullet">
    /// <item><c>state.json</c> — throwaway UI/session state that changes almost every launch.</item>
    /// <item><c>logs/</c> — per-session logs, recreatable and noisy.</item>
    /// <item><c>backups/</c> — the feature's own archives and index; backing them up would recurse.</item>
    /// <item><c>*.tmp</c> — atomic-write temporaries (they never outlive a write, but a crash can leave one).</item>
    /// </list>
    /// </summary>
    public static bool IsExcluded(string relativePath)
    {
        var path = BackupArchivePaths.Normalize(relativePath);

        if (string.Equals(path, "state.json", StringComparison.Ordinal))
        {
            return true;
        }

        if (path.StartsWith("logs/", StringComparison.Ordinal) ||
            path.StartsWith("backups/", StringComparison.Ordinal))
        {
            return true;
        }

        return path.EndsWith(".tmp", StringComparison.Ordinal);
    }
}
