namespace DayNote.Core.Storage;

/// <summary>
/// The result of writing a binder to disk: the path, the content hash (the version the next save expects
/// to find on disk), the exact serialized text (so callers need not re-serialize), and whether the file was
/// missing and written again.
/// </summary>
public sealed record SavedBinder(
    string Path,
    string ContentHash,
    string Text,
    bool Recreated = false);
