using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DayNote.Core.Storage;

/// <summary>
/// Binder paths keep their literal spelling. Existing-file aliases are resolved once at the background
/// storage boundary, rather than guessed by folding case during UI comparisons. The resolved name stays
/// valid across atomic replacement; an inode or file ID would not.
/// </summary>
public static partial class PathKey
{
    /// <summary>The absolute spelling, without conflating distinct case-sensitive files.</summary>
    public static string Normalize(string path) => Path.GetFullPath(path);

    /// <summary>Compares already-resolved names, without filesystem I/O.</summary>
    public static bool Equal(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    /// <summary>
    /// The existing file's resolved name, including its stored case. Called off the UI thread when a
    /// binder is loaded or the binder list is checked. Missing or inaccessible paths retain their spelling.
    /// </summary>
    public static string Resolve(string path) => ResolveExisting(path) ?? Normalize(path);

    /// <summary>Returns null when no native name could be read; callers keep their established identity.</summary>
    public static unsafe string? ResolveExisting(string path)
    {
        var full = Normalize(path);
        try
        {
            // A terminal file link has its own sibling assets and is itself replaced on save.
            // Resolve only its parent; coalescing it with its target would join two binder namespaces.
            if (!Directory.Exists(full) && new FileInfo(full).LinkTarget is not null)
            {
                var parent = Path.GetDirectoryName(full)!;
                var name = Path.GetFileName(full);
                var entries = Directory.EnumerateFileSystemEntries(parent).Select(Path.GetFileName).ToArray();
                var stored = entries.FirstOrDefault(entry => string.Equals(entry, name, StringComparison.Ordinal))
                    ?? entries.FirstOrDefault(entry => string.Equals(entry, name, StringComparison.OrdinalIgnoreCase));
                var resolvedParent = ResolveExisting(parent);
                return stored is null || resolvedParent is null ? null : Path.Combine(resolvedParent, stored);
            }
            if (OperatingSystem.IsMacOS())
            {
                // realpath has a fixed signature. Direct fcntl(F_GETPATH) interop is unsafe on
                // Darwin ARM64, where its variadic buffer argument uses a different calling convention.
                var bytes = new byte[1024]; // PATH_MAX on macOS.
                fixed (byte* buffer = bytes)
                {
                    if (GetMacPath(full, buffer) != IntPtr.Zero)
                    {
                        var end = Array.IndexOf(bytes, (byte)0);
                        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end);
                    }
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                using var handle = Directory.Exists(full)
                    ? OpenWindowsDirectory(WindowsExtendedPath(full), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)
                    : File.OpenHandle(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (handle.IsInvalid)
                    return null;
                var buffer = new char[32768]; // Windows extended paths can reach 32,767 characters.
                fixed (char* chars = buffer)
                {
                    var length = GetWindowsPath(handle, chars, (uint)buffer.Length, 0);
                    if (length > 0 && length < buffer.Length)
                    {
                        var resolved = new string(chars, 0, (int)length);
                        return resolved.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)
                            ? @"\\" + resolved[8..]
                            : resolved.StartsWith(@"\\?\", StringComparison.Ordinal) ? resolved[4..] : resolved;
                    }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    // Native calls must retain .NET's extended DOS/UNC path support.
    internal static string WindowsExtendedPath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            ? path
            : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial IntPtr GetMacPath(string path, byte* buffer);

    // Directory handles require FILE_FLAG_BACKUP_SEMANTICS. Zero desired access is enough to query
    // the final name; full sharing avoids reserving a directory against another process.
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle OpenWindowsDirectory(string path, uint access, uint sharing,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetWindowsPath(SafeFileHandle handle, char* buffer, uint length, uint flags);
}
