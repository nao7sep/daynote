namespace DayNote.Core.Identity;

/// <summary>
/// Generates note and binder identifiers: nanoids using the default URL-safe alphabet at
/// twenty-one characters. Identifiers double as attachment directory names and are compared
/// case-insensitively for Windows compatibility, so a freshly generated id is checked
/// case-insensitively against existing ids and regenerated on the (effectively impossible) collision.
/// </summary>
public static class IdGenerator
{
    /// <summary>A new 21-character URL-safe identifier.</summary>
    public static string New() => NanoidDotNet.Nanoid.Generate();

    /// <summary>
    /// A new identifier guaranteed not to collide case-insensitively with <paramref name="existingIds"/>.
    /// </summary>
    public static string NewUnique(IEnumerable<string> existingIds)
    {
        var taken = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        string id;
        do
        {
            id = New();
        }
        while (taken.Contains(id));

        return id;
    }
}
