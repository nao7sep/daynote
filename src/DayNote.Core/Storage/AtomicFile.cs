using System.Text;
using DayNote.Core.Backup;
using DayNote.Core.Identity;

namespace DayNote.Core.Storage;

/// <summary>
/// Atomic text writes: content is written to a temporary file, flushed to disk, and renamed over the
/// target, so a crash can lose the latest save but never leaves a damaged or empty file.
/// Files are UTF-8 without a byte-order mark; callers supply LF-terminated content. A write whose bytes
/// equal the file's is skipped, so the file's times, its backups and sync see only real changes
/// (content-lifecycle-conventions). On macOS, an existing file's ordinary permission mode is carried
/// onto the replacement.
/// </summary>
/// <remarks>
/// The app's one atomic text write: config.json and state.json through <see cref="JsonStore{T}"/>, and
/// every binder <c>.daynote</c> file through <see cref="BinderStore"/>. It is therefore where each write
/// reaches the backup history (data-backup-conventions, see <see cref="BackupStore.Record"/>): binders and
/// the settings are what the user creates and maintains, so they are recorded; state.json opts out, since
/// placement and selection are state, never the user's work.
/// </remarks>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content, bool recordBackup = true)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Path has no directory: {path}", nameof(path));
        var bytes = Utf8NoBom.GetBytes(content);
        if (HoldsExactly(fullPath, bytes))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var tempPath = TempPathFor(fullPath);

        try
        {
            // Restrict access at creation, not after writing: a private binder must never have
            // a broadly readable staging copy. Existing final modes are restored below;
            // new text files stay private. Windows retains the runtime's ordinary access model.
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (OperatingSystem.IsMacOS())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var stream = new FileStream(tempPath, options))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (OperatingSystem.IsMacOS() && File.Exists(fullPath))
            {
                File.SetUnixFileMode(tempPath, File.GetUnixFileMode(fullPath));
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        // After the rename: the file is exactly where it belongs, so hand the history
        // the exact bytes we just wrote (data-backup-conventions: strictly after the rename lands, so the
        // history never holds a version that never reached disk, and from the in-hand buffer, never a
        // reread that could capture another writer's content). Record only queues them for the history's
        // own thread and never throws, so the save neither waits on the history nor fails because of it.
        if (recordBackup)
        {
            BackupStore.Record(fullPath, bytes);
        }
    }

    /// <summary>
    /// Copies a user's file to a new name that nothing else holds: into a temp file beside it, then
    /// published under that name without replacing anything there, so the name never shows a partial
    /// copy. <see cref="File.Copy(string, string)"/> keeps the source's modified time and the metadata the
    /// destination volume supports (content-lifecycle-conventions). The temp is flushed before it is
    /// renamed, and a failed copy removes it.
    /// </summary>
    /// <remarks>
    /// The copy is a file the user adds, such as an attachment, which the backup history protects once, when
    /// it is added (data-backup-conventions). The caller records it with <see cref="BackupStore.RecordAdded"/>
    /// and the hash of what it copied, since only the caller knows that hash; the history then streams the
    /// copy from its published name and keeps it only if it still holds those bytes.
    /// </remarks>
    /// <exception cref="IOException">The destination already exists, or the copy failed.</exception>
    public static void CopyNew(string source, string destination) => CopyNew(source, destination, FlushCopiedFile);

    internal static void CopyNew(string source, string destination, Action<string> flushFile)
    {
        var fullPath = Path.GetFullPath(destination);
        var tempPath = TempPathFor(fullPath);
        try
        {
            File.Copy(source, tempPath);
            flushFile(tempPath);
            File.Move(tempPath, fullPath, overwrite: false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void FlushCopiedFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            RandomAccess.FlushToDisk(handle);
            return;
        }

        // Windows requires write access to flush. A copied read-only file keeps that state at
        // publication; only the private staging copy temporarily allows this handle to be opened.
        var attributes = File.GetAttributes(path);
        try
        {
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            RandomAccess.FlushToDisk(handle);
        }
        finally
        {
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes);
        }
    }

    // <stem>-<nanoid>.tmp, beside the target: one final extension stating the file's current role (a
    // temp), never a suffix dot-appended after the full target filename.
    private static string TempPathFor(string fullPath) => Path.Combine(
        Path.GetDirectoryName(fullPath)!,
        Path.GetFileNameWithoutExtension(fullPath) + "-" + IdGenerator.New() + ".tmp");

    private static bool HoldsExactly(string path, byte[] bytes)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
    }

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
            // Best effort: a temp left behind is debris, per the storage-path-conventions.
        }
    }
}
