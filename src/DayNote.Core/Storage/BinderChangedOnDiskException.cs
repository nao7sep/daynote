namespace DayNote.Core.Storage;

/// <summary>
/// Thrown by a binder save when the file on disk is no longer the version DayNote last loaded or wrote:
/// something outside DayNote changed it. Nothing was written; the user chooses whether their version or
/// the one on disk wins.
/// </summary>
public sealed class BinderChangedOnDiskException(string path, string diskHash)
    : Exception($"The binder at {path} changed outside DayNote since it was loaded or last saved.")
{
    /// <summary>The content hash of the version found on disk, which a save that keeps the user's version
    /// may then replace, and only that version.</summary>
    public string DiskHash { get; } = diskHash;
}
