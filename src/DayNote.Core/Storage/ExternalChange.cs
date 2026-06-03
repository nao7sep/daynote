namespace DayNote.Core.Storage;

/// <summary>
/// The result of comparing a notebook file on disk against the modification time and content hash
/// captured when it was loaded.
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
