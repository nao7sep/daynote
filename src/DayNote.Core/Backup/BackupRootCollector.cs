using DayNote.Core.Configuration;
using DayNote.Core.Storage;

namespace DayNote.Core.Backup;

/// <summary>
/// Discovers what to back up by reading the app's own state: the home root under <c>~/.daynote/</c> and
/// every known binder (each a <c>.daynote</c> file plus its sibling assets), wherever the user saved it.
/// Produces the stat'd candidates for <see cref="BackupPlan"/> and records a <see cref="BackupSkip"/> for
/// any dead or unreadable link. All I/O here is metadata only — directory walks and <see cref="FileInfo"/>;
/// file contents are read later, when a changed file is archived.
/// </summary>
public sealed class BackupRootCollector
{
    private readonly AppPaths _paths;
    private readonly BinderStore _binderStore = new();

    public BackupRootCollector(AppPaths paths) => _paths = paths;

    public CollectedRoots Collect()
    {
        var candidates = new List<BackupCandidate>();
        var skips = new List<BackupSkip>();

        CollectHomeRoot(candidates, skips);
        CollectBinders(candidates, skips);

        return new CollectedRoots(candidates, skips);
    }

    /// <summary>Walks <c>~/.daynote/</c>, pruning the excluded <c>logs/</c> and <c>backups/</c> subtrees
    /// rather than walking and discarding them (backups/ can grow large).</summary>
    private void CollectHomeRoot(List<BackupCandidate> candidates, List<BackupSkip> skips)
    {
        if (Directory.Exists(_paths.Root))
        {
            WalkHome(_paths.Root, _paths.Root, candidates, skips);
        }
    }

    private void WalkHome(string root, string directory, List<BackupCandidate> candidates, List<BackupSkip> skips)
    {
        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(directory);
        }
        catch (Exception ex)
        {
            skips.Add(new BackupSkip(directory, "could not enumerate: " + ex.Message));
            return;
        }

        foreach (var entry in entries)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception ex)
            {
                skips.Add(new BackupSkip(entry, "could not stat: " + ex.Message));
                continue;
            }

            // Never follow a symlink/junction — silently skip it (it is not the app's own data, and
            // following it risks a walk loop or an escape outside the root). Only real directories and
            // regular files are considered (data-backup conventions' traversal rules).
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var relative = BackupArchivePaths.Normalize(Path.GetRelativePath(root, entry));
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                // Prune an excluded subtree (logs/, backups/) instead of descending into it.
                if (!HomeRootExclusions.IsExcluded(relative + "/"))
                {
                    WalkHome(root, entry, candidates, skips);
                }
            }
            else if (!HomeRootExclusions.IsExcluded(relative))
            {
                AddCandidate(candidates, skips, entry, BackupArchivePaths.ForHomeFile(relative));
            }
        }
    }

    private void CollectBinders(List<BackupCandidate> candidates, List<BackupSkip> skips)
    {
        var state = LoadState(skips);
        if (state is null)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var known in state.Binders)
        {
            var binderPath = known.Path;
            if (string.IsNullOrWhiteSpace(binderPath) || !seen.Add(Path.GetFullPath(binderPath)))
            {
                continue;
            }

            if (!File.Exists(binderPath))
            {
                skips.Add(new BackupSkip(binderPath, "binder file not found (dead link)"));
                continue;
            }

            string binderId;
            try
            {
                binderId = _binderStore.Load(binderPath).Binder.Id;
            }
            catch (Exception ex)
            {
                skips.Add(new BackupSkip(binderPath, "unreadable binder: " + ex.Message));
                continue;
            }

            if (string.IsNullOrEmpty(binderId))
            {
                skips.Add(new BackupSkip(binderPath, "binder has no id"));
                continue;
            }

            AddCandidate(candidates, skips, binderPath,
                BackupArchivePaths.ForBinderFile(binderId, Path.GetFileName(binderPath)));

            CollectBinderAssets(binderPath, binderId, candidates, skips);
        }
    }

    private void CollectBinderAssets(
        string binderPath, string binderId, List<BackupCandidate> candidates, List<BackupSkip> skips)
    {
        var assetsDir = BinderStore.AssetsDirectory(binderPath);
        if (!Directory.Exists(assetsDir))
        {
            return;
        }

        foreach (var file in SafeListFiles(assetsDir, skips))
        {
            var relative = Path.GetRelativePath(assetsDir, file);
            AddCandidate(candidates, skips, file, BackupArchivePaths.ForBinderAsset(binderId, relative));
        }
    }

    private AppState? LoadState(List<BackupSkip> skips)
    {
        try
        {
            return new JsonStore<AppState>(_paths.StateFile).Load();
        }
        catch (Exception ex)
        {
            // A corrupt state file means binders cannot be enumerated this run; the home root (config.json)
            // is still captured. Record it and carry on.
            skips.Add(new BackupSkip(_paths.StateFile, "unreadable state, binders skipped: " + ex.Message));
            return null;
        }
    }

    private static void AddCandidate(
        List<BackupCandidate> candidates, List<BackupSkip> skips, string sourcePath, string archivePath)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            candidates.Add(new BackupCandidate(
                sourcePath, archivePath, info.Length, ToWholeSecondUtc(info.LastWriteTimeUtc)));
        }
        catch (Exception ex)
        {
            skips.Add(new BackupSkip(sourcePath, "could not stat: " + ex.Message));
        }
    }

    private static IReadOnlyList<string> SafeListFiles(string directory, List<BackupSkip> skips)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex)
        {
            skips.Add(new BackupSkip(directory, "could not enumerate: " + ex.Message));
            return Array.Empty<string>();
        }
    }

    private static DateTimeOffset ToWholeSecondUtc(DateTime lastWriteUtc)
    {
        var utc = DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc);
        return new DateTimeOffset(utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerSecond)), TimeSpan.Zero);
    }
}

/// <summary>The candidates and skips a collection pass produced.</summary>
public sealed record CollectedRoots(
    IReadOnlyList<BackupCandidate> Candidates,
    IReadOnlyList<BackupSkip> Skips);
