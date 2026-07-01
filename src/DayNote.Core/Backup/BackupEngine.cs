using System.IO.Compression;
using DayNote.Core.Storage;
using DayNote.Core.Time;

namespace DayNote.Core.Backup;

/// <summary>
/// Runs one backup pass and returns a <see cref="BackupReport"/>. It never throws for an expected problem
/// (a fatal error is captured in the report) and never logs — the caller logs the report. See the
/// data-backup conventions: change is size + mtime, the archive mirrors <c>~/.daynote/</c>, and the archive
/// is written and renamed into place <em>before</em> the index so a crash never records a phantom backup.
/// </summary>
public sealed class BackupEngine
{
    private readonly AppPaths _paths;

    public BackupEngine(AppPaths paths) => _paths = paths;

    /// <summary>Captures everything changed since the last run. <paramref name="now"/> is injected so the
    /// archive stamp is deterministic under test.</summary>
    public BackupReport Run(DateTimeOffset now)
    {
        try
        {
            return RunCore(now);
        }
        catch (Exception ex)
        {
            return new BackupReport { Fatal = ex };
        }
    }

    private BackupReport RunCore(DateTimeOffset now)
    {
        var (index, indexReset) = LoadIndex();

        var collected = new BackupRootCollector(_paths).Collect();
        var skips = new List<BackupSkip>(collected.Skips);

        var changed = BackupPlan.SelectChanged(collected.Candidates, index);
        if (changed.Count == 0)
        {
            return new BackupReport { NothingChanged = true, Skips = skips, IndexWasReset = indexReset };
        }

        var archivedAt = DayNoteTime.FileStamp(now);
        var archived = WriteArchive(archivedAt, changed, skips);
        if (archived.Count == 0)
        {
            // Every changed file failed to read at archive time; nothing was written, so nothing is recorded.
            return new BackupReport { NothingChanged = true, Skips = skips, IndexWasReset = indexReset };
        }

        foreach (var item in archived)
        {
            index.Entries.Add(new BackupIndexEntry
            {
                ArchivedAt = archivedAt,
                ArchivePath = item.ArchivePath,
                SizeBytes = item.SizeBytes,
                LastWriteUtc = DayNoteTime.ToIsoSeconds(item.LastWriteUtc),
            });
        }

        // Index second: the archive is already safely in place, so a crash here just re-captures next run.
        new JsonStore<BackupIndex>(_paths.BackupIndexFile).Save(index);

        return new BackupReport
        {
            ArchiveFileName = ArchiveFileName(archivedAt),
            FilesArchived = archived.Count,
            Skips = skips,
            IndexWasReset = indexReset,
        };
    }

    private (BackupIndex Index, bool Reset) LoadIndex()
    {
        try
        {
            return (new JsonStore<BackupIndex>(_paths.BackupIndexFile).Load() ?? new BackupIndex(), false);
        }
        catch
        {
            // A corrupt index is deleted and treated as empty: the run becomes a full backup, which costs
            // one redundant archive, never data.
            TryDelete(_paths.BackupIndexFile);
            return (new BackupIndex(), true);
        }
    }

    /// <summary>Writes the changed files to a temp zip and renames it into place, returning the files that
    /// were actually archived (a file unreadable at archive time is skipped, not recorded).</summary>
    private List<BackupCandidate> WriteArchive(
        string archivedAt, IReadOnlyList<BackupCandidate> changed, List<BackupSkip> skips)
    {
        Directory.CreateDirectory(_paths.BackupsDirectory);
        var finalPath = Path.Combine(_paths.BackupsDirectory, ArchiveFileName(archivedAt));
        var tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        var archived = new List<BackupCandidate>();
        try
        {
            using (var zip = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                foreach (var item in changed)
                {
                    try
                    {
                        zip.CreateEntryFromFile(item.SourcePath, item.ArchivePath);
                        archived.Add(item);
                    }
                    catch (Exception ex)
                    {
                        skips.Add(new BackupSkip(item.ArchivePath, "unreadable at archive time: " + ex.Message));
                    }
                }
            }

            if (archived.Count == 0)
            {
                TryDelete(tempPath);
                return archived;
            }

            File.Move(tempPath, finalPath, overwrite: true);
            return archived;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static string ArchiveFileName(string archivedAt) => "backup-" + archivedAt + ".zip";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort: a leftover temp is harmless and under backups/, which the walk excludes.
        }
    }
}
