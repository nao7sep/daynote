using System.Text;
using DayNote.Core.Identity;
using DayNote.Core.Models;
using DayNote.Core.Toml;

namespace DayNote.Core.Storage;

/// <summary>
/// Reads and writes <c>.daynote</c> binder files, capturing the content hash of each version it loads or
/// writes, and manages the matching <c>-assets</c> directory. This is the edge where binder file I/O lives;
/// serialization itself is pure and lives in <see cref="BinderTomlReader"/> and <see cref="BinderTomlWriter"/>.
/// The load and the write are virtual so a test can hold one at its file I/O while it starts a competing
/// action.
/// </summary>
/// <remarks>
/// A change made outside DayNote is noticed when DayNote next saves, not by watching the file: the save
/// reads the file it is about to replace and compares it with the version DayNote last loaded or wrote. The
/// read was needed anyway, to leave a file a newer DayNote wrote untouched (store-recovery-conventions), so
/// protecting an outside edit from being overwritten without a choice costs no monitoring at all.
/// </remarks>
public class BinderStore
{
    /// <summary>Loads a binder and the content hash of the version it read.</summary>
    /// <exception cref="NewerFormatException">The binder was written by a newer DayNote; it is left untouched.</exception>
    public virtual LoadedBinder Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var raw = File.ReadAllText(fullPath, Encoding.UTF8);
        var binder = BinderTomlReader.Read(raw);
        return new LoadedBinder(binder, fullPath, ContentHash.Sha256Hex(raw));
    }

    /// <summary>Serializes and atomically writes a new binder, returning its content hash and text.</summary>
    /// <exception cref="NewerFormatException">A file a newer DayNote wrote is already there; it is left untouched.</exception>
    public SavedBinder Save(string path, Binder binder) => SaveText(path, Serialize(binder), expectedHash: null);

    /// <summary>Serializes a binder to its TOML text — pure and in-memory, no I/O.</summary>
    public static string Serialize(Binder binder) => BinderTomlWriter.Write(binder);

    /// <summary>
    /// Atomically writes already-serialized binder text over the version DayNote last loaded or wrote, whose
    /// content hash is <paramref name="expectedHash"/>, and returns the new version's hash. Split from
    /// <see cref="Save"/> so a caller can serialize the (mutable, UI-owned) <see cref="Binder"/> on its own
    /// thread and move only the I/O to a background thread. When the file on disk is no longer that version,
    /// nothing is written: a file a newer DayNote wrote is intact data this build cannot read, and any other
    /// is a change the user decides about. A missing file is written again. With no
    /// <paramref name="expectedHash"/> (a new binder), only a newer DayNote's file is refused.
    /// </summary>
    /// <exception cref="NewerFormatException">The file was written by a newer DayNote; it is left untouched.</exception>
    /// <exception cref="BinderChangedOnDiskException">The file changed outside DayNote; it is left untouched.</exception>
    public virtual SavedBinder SaveText(string path, string text, string? expectedHash)
    {
        var fullPath = Path.GetFullPath(path);
        var existed = File.Exists(fullPath);
        if (existed)
        {
            var raw = File.ReadAllText(fullPath, Encoding.UTF8);
            var diskHash = ContentHash.Sha256Hex(raw);
            if (expectedHash is null || diskHash != expectedHash)
            {
                if (NewerVersion(raw) is { } newer)
                {
                    throw new NewerFormatException("binder", newer, FormatVersions.Binder);
                }

                if (expectedHash is not null)
                {
                    throw new BinderChangedOnDiskException(fullPath, diskHash);
                }
            }
        }

        AtomicFile.WriteAllText(fullPath, text);
        return new SavedBinder(fullPath, ContentHash.Sha256Hex(text), text, Recreated: expectedHash is not null && !existed);
    }

    // The version a newer DayNote recorded, if one did. A file that is not a binder at all is not newer.
    private static long? NewerVersion(string raw)
    {
        try
        {
            var version = BinderTomlReader.FormatVersion(raw);
            return version > FormatVersions.Binder ? version : null;
        }
        catch (BinderFormatException)
        {
            return null;
        }
    }

    /// <summary>The <c>&lt;basename&gt;-assets</c> directory beside the binder file.</summary>
    public static string AssetsDirectory(string binderPath)
    {
        var fullPath = Path.GetFullPath(binderPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var baseName = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, baseName + "-assets");
    }

    /// <summary>The per-note attachment directory <c>&lt;basename&gt;-assets/&lt;note-id&gt;/</c>.</summary>
    public static string NoteAssetsDirectory(string binderPath, string noteId) =>
        Path.Combine(AssetsDirectory(binderPath), noteId);

    /// <summary>Resolves a note's bare attachment filenames into absolute-path <see cref="Attachment"/> objects.</summary>
    public static IReadOnlyList<Attachment> ResolveAttachments(string binderPath, Note note)
    {
        var directory = NoteAssetsDirectory(binderPath, note.Id);
        var resolved = new List<Attachment>(note.Attachments.Count);
        foreach (var fileName in note.Attachments)
        {
            resolved.Add(new Attachment(fileName, Path.Combine(directory, fileName)));
        }

        return resolved;
    }
}
