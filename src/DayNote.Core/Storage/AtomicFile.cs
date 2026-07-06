using System.Runtime.InteropServices;
using System.Text;
using DayNote.Core.Backup;
using DayNote.Core.Identity;

namespace DayNote.Core.Storage;

/// <summary>
/// Atomic, durable text writes, ported from quickdeck: content is written to a temporary file,
/// flushed to disk, and renamed over the target; then the containing directory is flushed so the
/// rename itself survives a crash. Files are UTF-8 without a byte-order mark; callers are
/// responsible for supplying LF-terminated content.
/// </summary>
/// <remarks>
/// This is the single managed-text atomic-write choke point for the app: config.json and state.json
/// (via <see cref="JsonStore{T}"/>) and every binder <c>.daynote</c> file (via
/// <see cref="BinderStore"/>) all pass through here. That is exactly why the data-backup hook lives in
/// this one place — a managed-text write that bypassed this helper would be a silent backup gap, and
/// there is deliberately no second atomic-write path in the app. The store records the exact bytes just
/// written, strictly AFTER the rename lands (see <see cref="BackupStore.Record"/>).
/// </remarks>
public static partial class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Path has no directory: {path}", nameof(path));
        Directory.CreateDirectory(directory);

        // <stem>-<nanoid>.tmp, beside the target: one final extension stating the file's current role
        // (a temp), never a suffix dot-appended after the full target filename.
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var tempPath = Path.Combine(directory, stem + "-" + IdGenerator.New() + ".tmp");
        var bytes = Utf8NoBom.GetBytes(content);

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
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

        // After the rename and directory flush: the file is exactly where it belongs, so record the exact
        // bytes we just wrote (data-backup conventions — strictly AFTER the rename lands, so the history
        // never holds a version that never reached disk). We reuse the in-hand `bytes` buffer; the file is
        // never re-read (which could capture a concurrent writer's content). This sits OUTSIDE the write's
        // try/catch on purpose: the save has already fully succeeded, so a backup problem must never route
        // into the temp-delete-and-rethrow path. Record is itself best-effort — it catches, logs once, and
        // swallows every failure — so it can never throw here or break the save.
        BackupStore.Record(fullPath, bytes);
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
            // Best effort: a leftover temp file is harmless and will be overwritten by name reuse.
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
