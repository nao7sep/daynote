using System.Runtime.InteropServices;
using System.Text;
using DayNote.Core.Backup;
using DayNote.Core.Identity;

namespace DayNote.Core.Storage;

/// <summary>
/// Atomic, durable text writes: content is written to a temporary file, flushed to disk, and renamed
/// over the target; then the containing directory is flushed so the rename itself survives a crash.
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
public static partial class AtomicFile
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
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (OperatingSystem.IsMacOS() && File.Exists(fullPath))
            {
                File.SetUnixFileMode(tempPath, File.GetUnixFileMode(fullPath));
            }

            File.Move(tempPath, fullPath, overwrite: true);

            // Flushing the temp file's data (above) is not enough: a crash right after the rename can
            // leave the directory entry pointing at the old inode, silently rolling the save back to
            // the previous version. Flushing the containing directory closes that window.
            FlushDirectory(directory);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        // After the rename and directory flush: the file is exactly where it belongs, so hand the history
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
    /// destination volume supports (content-lifecycle-conventions). A failed copy removes its temp.
    /// </summary>
    /// <remarks>
    /// The copy is a file the user adds, such as an attachment, which the backup history protects once, when
    /// it is added (data-backup-conventions). The caller records it with <see cref="BackupStore.RecordAdded"/>
    /// and the hash of what it copied, since only the caller knows that hash; the history then streams the
    /// copy from its published name and keeps it only if it still holds those bytes.
    /// </remarks>
    /// <exception cref="IOException">The destination already exists, or the copy failed.</exception>
    public static void CopyNew(string source, string destination)
    {
        var fullPath = Path.GetFullPath(destination);
        var tempPath = TempPathFor(fullPath);
        try
        {
            File.Copy(source, tempPath);
            File.Move(tempPath, fullPath, overwrite: false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
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

    /// <summary>
    /// Makes the most recent rename durable by flushing the containing directory. .NET exposes no
    /// portable directory flush, so this drops to the OS primitive on Unix (where DayNote primarily
    /// runs). On Windows there is no equivalent non-privileged call; NTFS metadata journaling keeps
    /// the rename consistent (never a torn or vanished file), so the directory flush is a Unix-only step.
    /// </summary>
    private static void FlushDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fd = open(directory, O_RDONLY);
        if (fd < 0)
        {
            throw new IOException($"Could not open directory to flush ('{directory}'); errno {Marshal.GetLastPInvokeError()}.");
        }

        try
        {
            if (fsync(fd) != 0)
            {
                throw new IOException($"Could not flush directory ('{directory}'); errno {Marshal.GetLastPInvokeError()}.");
            }
        }
        finally
        {
            close(fd);
        }
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

    // O_RDONLY is 0 on both Linux and macOS; a read-only handle is sufficient to fsync a directory.
    private const int O_RDONLY = 0;

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int open(string path, int flags);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int fsync(int fd);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int close(int fd);
}
