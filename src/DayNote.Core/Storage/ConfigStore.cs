using System.Text.Json;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>
/// Reads config.json once and writes it from memory, per the config-sets-conventions and the
/// storage-path-conventions. A save never deletes what it did not understand: keys this build does not
/// know, and an authored set (the binder list, the text styles) that failed its check, are written back
/// as they were stored until the user changes that set.
/// </summary>
public sealed class ConfigStore(string path, Action<string> warn)
{
    private readonly JsonStore<Dictionary<string, JsonElement>> _store = new(path, FormatVersions.Config);

    // The map the file holds as of the last load or save; null while there is no file.
    private Dictionary<string, JsonElement>? _onDisk;

    // Stored entries a save writes back unchanged, and the sets as loaded, to tell whether the user has
    // since changed a kept set.
    private Dictionary<string, JsonElement> _kept = new();
    private JsonElement _loaded;

    public AppConfig Load()
    {
        _onDisk = _store.Load();
        var stored = _onDisk ?? new();
        var config = ConfigSets.Read(stored, warn);
        _kept = ConfigSets.Kept(stored);
        _loaded = JsonSerializer.SerializeToElement(config, DayNoteJson.Options);
        return config;
    }

    public void Save(AppConfig config)
    {
        var sets = ConfigSets.UserSets(config);
        var values = JsonSerializer.SerializeToElement(config, DayNoteJson.Options);
        foreach (var (key, stored) in _kept.ToArray())
        {
            if (!values.TryGetProperty(key, out var value))
            {
                sets[key] = stored;
            }
            else if (JsonElement.DeepEquals(value, _loaded.GetProperty(key)))
            {
                sets[key] = stored;
            }
            else
            {
                // The user replaced the set; its stored value has served its purpose.
                _kept.Remove(key);
            }
        }

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
