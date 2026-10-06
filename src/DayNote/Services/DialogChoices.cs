namespace DayNote.Services;

/// <summary>What to do when a binder file was modified outside the application while it had unsaved edits.</summary>
public enum ExternalChangeChoice
{
    /// <summary>Discard in-memory edits and reload from disk.</summary>
    ReloadFromDisk,

    /// <summary>Keep the in-memory buffer (the next save will overwrite the external change).</summary>
    KeepMine,
}

/// <summary>What to do when a quit the user started could not save the binder's edits.</summary>
public enum UnsavedQuitChoice
{
    /// <summary>The question was dismissed: the quit stays cancelled and the app stays open.</summary>
    Stay,

    /// <summary>Try the quit's save again.</summary>
    Retry,

    /// <summary>Quit without the unsaved edits.</summary>
    QuitAnyway,
}
