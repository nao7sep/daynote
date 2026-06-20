namespace DayNote.Core.Storage;

/// <summary>
/// The result of comparing a binder file on disk against the content hash captured when it was
/// loaded (the hash alone; modification time is deliberately not used).
/// </summary>
public enum ExternalChange
{
    /// <summary>The file is unchanged since it was loaded.</summary>
    None,

    /// <summary>The file content differs from what was loaded.</summary>
    Modified,

    /// <summary>The file no longer exists.</summary>
    Deleted,
}
