namespace DayNote.Desktop.Services;

/// <summary>What to do when a notebook is already locked by another instance.</summary>
public enum LockedNotebookChoice
{
    OpenReadOnly,
    Cancel,
}

/// <summary>What to do when a notebook file was modified outside the application while it had unsaved edits.</summary>
public enum ExternalChangeChoice
{
    /// <summary>Discard in-memory edits and reload from disk.</summary>
    ReloadFromDisk,

    /// <summary>Keep the in-memory buffer (the next save will overwrite the external change).</summary>
    KeepMine,
}
