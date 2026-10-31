using System.Text.Json;
using System.Text.Json.Nodes;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>
/// A typed JSON store for one file, the configuration or the state, per the store-recovery-conventions.
/// The file's identity header (<see cref="FileHeader"/>) comes first in every save. A load only reads: a
/// file that cannot be parsed, has no header naming this file's owner and kind, or does not fit the shape throws
/// <see cref="InvalidDataException"/>; a file recording a newer format throws
/// <see cref="NewerFormatException"/>; a read error throws as it is. The file is never moved or changed by
/// a load, so the caller decides what each failure means: the settings halt and stay untouched, the
/// disposable state falls back to defaults. Writes are atomic and end with a trailing newline.
/// </summary>
public sealed class JsonStore<T>
    where T : class
{
    private readonly string _path;
    private readonly FileFormat _format;
    private readonly bool _recordBackup;
    private readonly bool _replacesUnreadable;

    // Set when the last load refused the file: it then stays byte-identical, whatever saves later.
    private bool _refused;

    /// <param name="replacesUnreadable">
    /// The file is disposable state: one that cannot be read may be replaced by the next save. A newer
    /// file is never replaced, and no other store's refused file is either (store-recovery-conventions).
    /// </param>
    public JsonStore(string path, FileFormat format, bool recordBackup = true, bool replacesUnreadable = false)
    {
        _replacesUnreadable = replacesUnreadable;
        _path = path;
        _format = format;
        _recordBackup = recordBackup;
    }

    /// <exception cref="InvalidDataException">The file is not a store this build can read.</exception>
    /// <exception cref="NewerFormatException">The file records a newer format than this store reads.</exception>
    public T? Load()
    {
        try
        {
            var loaded = Read();
            _refused = false;
            return loaded;
        }
        catch (NewerFormatException)
        {
            _refused = true;
            throw;
        }
        catch
        {
            _refused = !_replacesUnreadable;
            throw;
        }
    }

    private T? Read()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var json = File.ReadAllText(_path);
        JsonObject root;
        long version;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("The file is not a JSON object.");
            version = FileHeader.TakeJson(root, _format)
                ?? throw new JsonException($"The file has no {FileHeader.Owner} {_format.Kind} header.");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // ArgumentException: JsonObject refuses a repeated key, which DayNote never writes.
            throw Unreadable(ex);
        }

        if (version > _format.Version)
        {
            throw new NewerFormatException(Path.GetFileName(_path), version, _format.Version);
        }

        try
        {
            return root.Deserialize<T>(DayNoteJson.Options);
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex);
        }
    }

    /// <exception cref="InvalidOperationException">The last load refused the file, which is never changed.</exception>
    public void Save(T value)
    {
        if (_refused)
        {
            throw new InvalidOperationException($"{Path.GetFileName(_path)} was refused at load and is left as it is.");
        }

        var body = JsonSerializer.SerializeToNode(value, DayNoteJson.Options) as JsonObject
            ?? throw new InvalidOperationException($"{typeof(T).Name} does not serialize to a JSON object.");
        var members = body.ToList();
        body.Clear();
        var root = FileHeader.JsonRoot(_format);
        foreach (var (key, member) in members)
        {
            root[key] = member;
        }

        var json = root.ToJsonString(DayNoteJson.Options);
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }

        AtomicFile.WriteAllText(_path, json, _recordBackup);
    }

    private InvalidDataException Unreadable(Exception error) =>
        new($"{Path.GetFileName(_path)} is not a file this build can read.", error);
}
