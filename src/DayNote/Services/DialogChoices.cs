namespace DayNote.Services;

/// <summary>What to do when a save finds the binder file modified outside the application.</summary>
public enum ExternalChangeChoice
{
    /// <summary>Discard in-memory edits and reload from disk.</summary>
    ReloadFromDisk,

    /// <summary>Keep the in-memory version, which replaces the version on disk the user was asked about.</summary>
    KeepMine,

    /// <summary>
    /// Unanswered: the question closed because the app or the operating system closed it. The user cannot
    /// dismiss it, since each answer gives up a version; nothing is written or reloaded.
    /// </summary>
    Unanswered,
}

/// <summary>What to do when a quit the user started could not save the binder's edits.</summary>
public enum UnsavedQuitChoice
{
    /// <summary>Cancel, or the question was dismissed: the quit stays cancelled and the app stays open.</summary>
    Stay,

    /// <summary>Try the quit's save again.</summary>
    Retry,

    /// <summary>Quit without the unsaved edits.</summary>
    QuitAnyway,
}
