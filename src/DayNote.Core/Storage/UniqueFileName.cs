namespace DayNote.Core.Storage;

/// <summary>
/// Picks a filename that does not collide with any existing name in a directory. The existence check
/// is case-insensitive (per storage-path-conventions: a name that differs only in case would silently
/// clobber a sibling on macOS/Windows), while the human-readable filename is preserved verbatim —
/// only disambiguated with a " (n)" suffix when needed, never lowercased or otherwise mangled.
/// Pure and filesystem-free: the caller supplies the existing names; the ViewModel passes the real
/// directory listing.
/// </summary>
public static class UniqueFileName
{
    /// <summary>
    /// Returns <paramref name="fileName"/> unchanged when no existing name matches it
    /// case-insensitively; otherwise appends " (1)", " (2)", … before the extension until the
    /// candidate is free. Comparison against <paramref name="existingNames"/> is case-insensitive.
    /// </summary>
    public static string Pick(IEnumerable<string> existingNames, string fileName)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var candidate = fileName;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;
        while (taken.Contains(candidate))
        {
            candidate = $"{name} ({counter}){extension}";
            counter++;
        }

        return candidate;
    }
}
