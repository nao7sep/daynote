using System.Text.Json;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>
/// Reads config.json once and writes it from memory, per the config-sets-conventions and the
/// storage-path-conventions.
/// </summary>
public sealed class ConfigStore(string path, Action<string> warn)
{
    private readonly JsonStore<Dictionary<string, JsonElement>> _store = new(path);

    // The map the file holds as of the last load or save; null while there is no file.
    private Dictionary<string, JsonElement>? _onDisk;

    public AppConfig Load()
    {
        _onDisk = _store.Load();
        return ConfigSets.Read(_onDisk ?? new(), warn);
    }

    public void Save(AppConfig config)
    {
        var sets = ConfigSets.UserSets(config);
        if (_onDisk is null ? sets.Count == 0 : SameSets(_onDisk, sets))
        {
            return;
        }

        _store.Save(sets);
        _onDisk = sets;
    }

    private static bool SameSets(Dictionary<string, JsonElement> stored, Dictionary<string, JsonElement> sets) =>
        stored.Count == sets.Count
        && sets.All(set => stored.TryGetValue(set.Key, out var value) && JsonElement.DeepEquals(value, set.Value));
}
