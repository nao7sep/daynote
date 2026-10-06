using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using DayNote.Core.Backup;
using DayNote.Core.Identity;

namespace DayNote.Core.Storage;

/// <summary>
/// Atomic, durable text writes: content is written to a temporary file, flushed to disk, and renamed
/// over the target; then the containing directory is flushed so the rename itself survives a crash.
/// Files are UTF-8 without a byte-order mark; callers supply LF-terminated content. A write whose bytes
/// equal the file's is skipped, so the file's times, its backups and sync see only real changes
/// (content-lifecycle-conventions). A replace keeps what it can of the file it replaces: on macOS, where
/// the rename installs the temp file's own metadata, the original's permission mode, ACL and extended
/// attributes (Finder tags among them) are carried onto the temp first.
/// </summary>
/// <remarks>
/// The app's one atomic text write: config.json and state.json through <see cref="JsonStore{T}"/>, and
/// every binder <c>.daynote</c> file through <see cref="BinderStore"/>. It is therefore where each write
/// reaches the backup history (data-backup-conventions, see <see cref="BackupStore.Record"/>), unless the
/// caller opts out, as state.json does.
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
                CopyReplaceMetadata(fullPath, tempPath);
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

    /// <summary>
    /// Carries <paramref name="original"/>'s ACL, extended attributes and permission mode onto
    /// <paramref name="temp"/>, never its times or ownership, which <c>COPYFILE_STAT</c> would copy too. A
    /// volume that cannot hold ACLs or extended attributes answers <c>ENOTSUP</c> and keeps what it can.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static void CopyReplaceMetadata(string original, string temp)
    {
        using (var from = File.OpenHandle(original))
        using (var to = File.OpenHandle(temp, FileMode.Open, FileAccess.ReadWrite))
        {
            if (fcopyfile(from, to, IntPtr.Zero, COPYFILE_ACL | COPYFILE_XATTR) < 0
                && Marshal.GetLastPInvokeError() is var errno && errno != ENOTSUP)
            {
                throw new IOException($"Could not copy the metadata of '{original}'; errno {errno}.");
            }
        }

        // After the extended attributes, which a read-only mode would refuse.
        File.SetUnixFileMode(temp, File.GetUnixFileMode(original));
    }

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

    // From <copyfile.h> and macOS <sys/errno.h>.
    private const uint COPYFILE_ACL = 1 << 0;
    private const uint COPYFILE_XATTR = 1 << 2;
    private const int ENOTSUP = 45;

    [LibraryImport("libc", SetLastError = true)]
    private static partial int fcopyfile(SafeFileHandle from, SafeFileHandle to, IntPtr state, uint flags);
}
