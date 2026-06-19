namespace DayNote.Core.Storage;

/// <summary>
/// Normalizes filesystem paths into a stable key used for per-notebook locks and equality checks.
/// Paths are compared case-insensitively for Windows compatibility; on a case-sensitive filesystem
/// this is an accepted tradeoff.
/// </summary>
public static class PathKey
{
    /// <summary>The full path, lowercased, used as a comparison key.</summary>
    public static string Normalize(string path) => Path.GetFullPath(path).ToLowerInvariant();

    /// <summary>Whether two paths refer to the same file under case-insensitive comparison.</summary>
    public static bool Equal(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}
