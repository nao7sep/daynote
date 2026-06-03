namespace DayNote.Core.Storage;

/// <summary>The result of attempting to record a backup version.</summary>
public enum BackupOutcome
{
    /// <summary>A new version row was inserted.</summary>
    Inserted,

    /// <summary>Identical content already exists for this notebook; nothing was inserted.</summary>
    Duplicate,

    /// <summary>A version was recorded too recently; skipped by the per-interval throttle.</summary>
    Throttled,
}
