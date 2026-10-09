using DayNote.Core.Models;

namespace DayNote.Core.Storage;

/// <summary>
/// A binder read from disk together with the content hash of the version read, which the next save
/// expects to find on disk.
/// </summary>
public sealed record LoadedBinder(
    Binder Binder,
    string Path,
    string ContentHash);
