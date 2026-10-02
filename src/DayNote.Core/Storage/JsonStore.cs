using System.Text.Json;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>
/// A typed JSON store for one file, the configuration or the state. A file that cannot be parsed is
/// set aside under its <c>.invalid</c> name and the load returns <c>null</c>, per the
/// store-recovery-conventions; a failed move throws, so the next save never writes over the bytes. A
/// read error is not a parse failure and throws. Writes are atomic and end with a trailing newline.
/// </summary>
public sealed class JsonStore<T>
    where T : class
{
    private readonly string _path;
    private readonly bool _recordBackup;

    public JsonStore(string path, bool recordBackup = true)
    {
        _path = path;
        _recordBackup = recordBackup;
    }

    public T? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var json = File.ReadAllText(_path);
        try
        {
            return JsonSerializer.Deserialize<T>(json, DayNoteJson.Options);
        }
        catch (JsonException)
        {
            var quarantinePath = Path.Combine(
                Path.GetDirectoryName(_path) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(_path)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-utc.invalid");
            File.Move(_path, quarantinePath);
            QuarantineJournal.Record(quarantinePath);
            return null;
        }
    }

    public void Save(T value)
    {
        var json = JsonSerializer.Serialize(value, DayNoteJson.Options);
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }

        AtomicFile.WriteAllText(_path, json, _recordBackup);
    }
}
