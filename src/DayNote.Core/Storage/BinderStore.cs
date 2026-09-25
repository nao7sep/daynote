using System.Text;
using DayNote.Core.Identity;
using DayNote.Core.Models;
using DayNote.Core.Toml;

namespace DayNote.Core.Storage;

/// <summary>
/// Reads and writes <c>.daynote</c> binder files, capturing the content hash needed for
/// external-change detection, and manages the matching <c>-assets</c> directory.
/// This is the edge where binder file I/O lives; serialization itself is pure and lives in
/// <see cref="BinderTomlReader"/> and <see cref="BinderTomlWriter"/>.
/// </summary>
public sealed class BinderStore
{
    /// <summary>Loads a binder and records the content-hash baseline for external-change detection.</summary>
    public LoadedBinder Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var raw = File.ReadAllText(fullPath, Encoding.UTF8);
        var binder = BinderTomlReader.Read(raw);
        return new LoadedBinder(binder, fullPath, ContentHash.Sha256Hex(raw));
    }

    /// <summary>Serializes and atomically writes a binder, returning the new baseline and text.</summary>
    public SavedBinder Save(string path, Binder binder) => SaveText(path, Serialize(binder));

    /// <summary>Serializes a binder to its TOML text — pure and in-memory, no I/O.</summary>
    public static string Serialize(Binder binder) => BinderTomlWriter.Write(binder);

    /// <summary>
    /// Atomically writes already-serialized binder text and returns the new baseline. Split out from
    /// <see cref="Save"/> so a caller can serialize the (mutable, UI-owned) <see cref="Binder"/> to an
    /// immutable string synchronously and then move only the I/O — the atomic write, fsync, and backup
    /// insert — to a background thread, with no risk of a concurrent edit touching the binder while it
    /// is being written.
    /// </summary>
    public SavedBinder SaveText(string path, string text)
    {
        var fullPath = Path.GetFullPath(path);
        AtomicFile.WriteAllText(fullPath, text);
        return new SavedBinder(fullPath, ContentHash.Sha256Hex(text), text);
    }

    /// <summary>
    /// Compares the file on disk against a load-time content-hash baseline. The hash is always
    /// computed (modification time is not used as a short-circuit), so an external edit that lands
    /// within the same coarse filesystem timestamp tick is still detected.
    /// </summary>
    public ExternalChange CheckExternalChange(string path, string loadedHash)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return ExternalChange.Deleted;
        }

        return ComputeHash(fullPath) == loadedHash ? ExternalChange.None : ExternalChange.Modified;
    }

    /// <summary>The content hash of the file on disk, used to (re)establish an external-change baseline.</summary>
    public string ComputeHash(string path) =>
        ContentHash.Sha256Hex(File.ReadAllText(Path.GetFullPath(path), Encoding.UTF8));

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
