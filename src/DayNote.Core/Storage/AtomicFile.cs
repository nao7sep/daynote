using System.Text;

namespace DayNote.Core.Storage;

/// <summary>
/// Atomic text writes, ported from quickdeck: content is written to a temporary file, flushed to
/// disk, and renamed over the target. Files are UTF-8 without a byte-order mark; callers are
/// responsible for supplying LF-terminated content.
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException($"Path has no directory: {path}", nameof(path));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var bytes = Utf8NoBom.GetBytes(content);

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
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
}
