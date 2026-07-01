namespace DayNote.Core.Backup;

/// <summary>
/// Pure mapping from a file's role to its entry path within the archive, which mirrors what
/// <c>~/.daynote/</c> would contain if binders were stored internally (see the data-backup conventions):
/// home files at their natural relative path, binders under <c>binders/&lt;binderId&gt;/</c>, and a binder's
/// attachments under <c>binders/&lt;binderId&gt;/assets/</c>. All entry paths use forward slashes.
/// </summary>
public static class BackupArchivePaths
{
    /// <summary>Normalizes a filesystem-relative path to a forward-slash archive path.</summary>
    public static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    /// <summary>A file that lives directly under <c>~/.daynote/</c>: its relative path is the archive path (<c>config.json</c>).</summary>
    public static string ForHomeFile(string relativePath) => Normalize(relativePath);

    /// <summary>The binder's own <c>.daynote</c> file: <c>binders/&lt;binderId&gt;/&lt;fileName&gt;</c>.</summary>
    public static string ForBinderFile(string binderId, string fileName) =>
        $"binders/{binderId}/{Normalize(fileName)}";

    /// <summary>An attachment under the binder's assets directory: <c>binders/&lt;binderId&gt;/assets/&lt;relative&gt;</c>.
    /// The on-disk <c>&lt;name&gt;-assets/</c> folder collapses to <c>assets/</c>, already namespaced by the id.</summary>
    public static string ForBinderAsset(string binderId, string relativeToAssets) =>
        $"binders/{binderId}/assets/{Normalize(relativeToAssets)}";
}
