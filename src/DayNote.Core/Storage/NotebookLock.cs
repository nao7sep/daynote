using DayNote.Core.Identity;

namespace DayNote.Core.Storage;

/// <summary>
/// A per-notebook lock that lets multiple application instances run concurrently as long as they
/// open different notebooks. Ownership is the exclusively-held file handle (FileShare.None) on a
/// persistent sentinel file under <c>~/.daynote/locks/</c>, keyed by the notebook's case-insensitive
/// path so the notebook's own directory stays clean. A second instance opening the same notebook
/// fails to acquire the handle and is told the notebook is in use. The sentinel file is left on
/// disk between sessions deliberately: deleting it on release would open a window in which one
/// instance unlinks the path while another recreates it, letting two instances both believe they
/// own the notebook. On Unix this relies on advisory locking.
/// </summary>
public sealed class NotebookLock : IDisposable
{
    private FileStream? _stream;

    private NotebookLock(FileStream stream) => _stream = stream;

    /// <summary>Attempts to acquire the lock; returns null if another instance already holds it.</summary>
    public static NotebookLock? TryAcquire(AppPaths paths, string notebookPath)
    {
        Directory.CreateDirectory(paths.LocksDirectory);
        var name = ContentHash.Sha256Hex(PathKey.Normalize(notebookPath))[..16] + ".lock";
        var lockPath = Path.Combine(paths.LocksDirectory, name);

        try
        {
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
            return new NotebookLock(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
