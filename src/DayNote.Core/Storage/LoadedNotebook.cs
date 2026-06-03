using DayNote.Core.Models;

namespace DayNote.Core.Storage;

/// <summary>
/// A notebook read from disk together with the content hash captured at load time, used later to
/// detect external modification.
/// </summary>
public sealed record LoadedNotebook(
    Notebook Notebook,
    string Path,
    string ContentHash);
