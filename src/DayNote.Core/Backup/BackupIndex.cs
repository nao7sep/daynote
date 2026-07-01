namespace DayNote.Core.Backup;

/// <summary>
/// The backup change ledger, serialized to <c>~/.daynote/backups/index.json</c>. It records one entry
/// per captured file state and is both the change ledger (deciding what a run must capture) and the
/// table used to locate a lost file later. See the data-backup conventions.
/// </summary>
public sealed class BackupIndex
{
    public List<BackupIndexEntry> Entries { get; set; } = new();
}

/// <summary>
/// One captured file state. Fields are declared in the conventional order — the JSON serializer
/// preserves declaration order — so a record reads <c>{ archivedAt, archivePath, sizeBytes, lastWriteUtc }</c>.
/// There is no content hash: change is detected from size and modification time (see the data-backup
/// conventions).
/// </summary>
public sealed class BackupIndexEntry
{
    /// <summary>The capturing run's UTC file stamp (<c>yyyymmdd-hhmmss-utc</c>). Also the stem of that run's
    /// archive, so the zip holding this entry is <c>backup-&lt;archivedAt&gt;.zip</c> — derived, never stored.</summary>
    public string ArchivedAt { get; set; } = string.Empty;

    /// <summary>The file's full entry path within the zip, e.g. <c>binders/&lt;binderId&gt;/assets/&lt;noteId&gt;/file</c>.</summary>
    public string ArchivePath { get; set; } = string.Empty;

    /// <summary>The file's size in bytes at capture time.</summary>
    public long SizeBytes { get; set; }

    /// <summary>The file's last-write time in UTC, truncated to the whole second (<c>yyyy-MM-ddTHH:mm:ssZ</c>).</summary>
    public string LastWriteUtc { get; set; } = string.Empty;
}
