using System.Text.Json;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>Reads config.json and writes it from memory, per the config-sets-conventions.</summary>
public sealed class ConfigStore(string path, Action<string> warn)
{
    private readonly JsonStore<Dictionary<string, JsonElement>> _store = new(path);

    public AppConfig Load() => ConfigSets.Read(_store.Load() ?? new(), warn);

    public void Save(AppConfig config)
    {
        var sets = ConfigSets.UserSets(config);
        var stored = _store.Load();
        if (stored is null ? sets.Count == 0 : SameSets(stored, sets))
        {
            return;
        }

        _store.Save(sets);
    }

    private static bool SameSets(Dictionary<string, JsonElement> stored, Dictionary<string, JsonElement> sets) =>
        stored.Count == sets.Count
        && sets.All(set => stored.TryGetValue(set.Key, out var value) && JsonElement.DeepEquals(value, set.Value));
}
