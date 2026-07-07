using DayNote.Core.Models;

namespace DayNote.Core.Storage;

/// <summary>
/// A binder read from disk together with the content hash captured at load time, used later to
/// detect external modification.
/// </summary>
public sealed record LoadedBinder(
    Binder Binder,
    string Path,
    string ContentHash);
