using System.Text.Json;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>
/// A typed JSON store for a single file, used for the configuration and state files. Loads return
/// <c>null</c> when the file does not exist (first run); a corrupt file throws so the caller can
/// gate writes and avoid overwriting good data after a failed load. Writes are atomic and end with
/// a trailing newline.
/// </summary>
public sealed class JsonStore<T>
    where T : class
{
    private readonly string _path;

    public JsonStore(string path) => _path = path;

    public T? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<T>(json, DayNoteJson.Options);
    }

    public void Save(T value)
    {
        var json = JsonSerializer.Serialize(value, DayNoteJson.Options);
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }

        AtomicFile.WriteAllText(_path, json);
    }
}
