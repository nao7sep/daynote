using System.Text.Json;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>
/// A typed JSON store for a single file, used for the configuration and state files — both
/// rebuildable (settings are re-authored, view state is rebuilt by use), so a present-but-corrupt
/// file is quarantined aside to its <c>.invalid</c> name and the load returns <c>null</c>: launch
/// proceeds, and first-run materialization reseeds config in the same launch (storage-path
/// conventions). The quarantine move either lands or its failure propagates — falling through to
/// defaults with the corrupt bytes in place would let the next save overwrite them. An I/O read
/// error is not corruption and still throws. Writes are atomic and end with a trailing newline.
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

        AtomicFile.WriteAllText(_path, json);
    }

    /// <summary>
    /// Creates the file from <paramref name="value"/> only when it does not yet exist, so a built-in
    /// defaultable file (config.json) is present on disk after the first run rather than appearing only
    /// once the user first changes a setting — see the storage-path conventions' "Materializing settings
    /// on first run". The single trigger is absence: an existing file is never inspected or overwritten,
    /// the one check that cannot corrupt a good (possibly hand-edited) file. The file is produced through
    /// <see cref="Save"/> — the same serializer the normal save path uses, not a hand-built literal.
    /// Returns true when a file was created.
    /// </summary>
    public bool CreateIfMissing(T value)
    {
        if (File.Exists(_path))
        {
            return false;
        }

        Save(value);
        return true;
    }
}
