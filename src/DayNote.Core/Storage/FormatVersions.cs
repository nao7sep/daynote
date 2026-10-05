namespace DayNote.Core.Storage;

/// <summary>
/// The format version of each store DayNote writes: one integer per format, independent of the app's
/// version and of the other formats, per the store-recovery-conventions. A file recording no version
/// reads as 1; a file recording a higher version than these is newer than this build and is left alone.
/// </summary>
public static class FormatVersions
{
    /// <summary>A binder's <c>.daynote</c> TOML file (<c>format_version</c>).</summary>
    public const int Binder = 1;

    /// <summary><c>config.json</c> (<c>formatVersion</c>).</summary>
    public const int Config = 1;

    /// <summary><c>state.json</c> (<c>formatVersion</c>).</summary>
    public const int State = 1;

    /// <summary><c>records.sqlite3</c> (<c>PRAGMA user_version</c>).</summary>
    public const int Records = 1;

    /// <summary><c>backups.sqlite3</c> (<c>PRAGMA user_version</c>).</summary>
    public const int Backups = 1;
}
