namespace DayNote.Services;

/// <summary>What to do when a binder file was modified outside the application while it had unsaved edits.</summary>
public enum ExternalChangeChoice
{
    /// <summary>Discard in-memory edits and reload from disk.</summary>
    ReloadFromDisk,

    /// <summary>Keep the in-memory buffer (the next save will overwrite the external change).</summary>
    KeepMine,
}
