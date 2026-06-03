namespace DayNote.Core.Models;

/// <summary>
/// A resolved attachment: a note's bare filename paired with its absolute on-disk path. Produced
/// by the storage layer for presentation; the persisted note only stores the <see cref="FileName"/>.
/// </summary>
public sealed record Attachment(string FileName, string FullPath)
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff",
    };

    /// <summary>Whether this attachment is an image that can be shown as a thumbnail.</summary>
    public bool IsImage => ImageExtensions.Contains(Path.GetExtension(FileName));
}
