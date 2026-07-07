namespace DayNote.Core.Storage;

/// <summary>
/// The result of writing a binder to disk: the path, the content hash (the new baseline for
/// external-change detection), and the exact serialized text (so callers need not re-serialize).
/// </summary>
public sealed record SavedBinder(
    string Path,
    string ContentHash,
    string Text);
