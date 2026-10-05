namespace DayNote.Core.Storage;

/// <summary>
/// Thrown when a store records a format version newer than this build reads. The store is intact data
/// this build cannot read, not a corrupt one: it is reported and left exactly as it is, never set aside,
/// reset or written to (store-recovery-conventions).
/// </summary>
public sealed class NewerFormatException(string store, long found, int supported)
    : Exception($"{store} records format version {found}; this build reads up to {supported}.")
{
    /// <summary>The store, as the diagnostic names it.</summary>
    public string Store { get; } = store;

    /// <summary>The version the store records.</summary>
    public long Found { get; } = found;

    /// <summary>The newest version this build reads.</summary>
    public int Supported { get; } = supported;
}
