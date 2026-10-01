using System.Text.Json;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>Persists the user's whole sets; loading absent sets never writes their built-ins.</summary>
public sealed class ConfigStore(string path, Action<string> warn)
{
    private readonly JsonStore<Dictionary<string, JsonElement>> _store = new(path);

    public AppConfig Load() => ConfigSets.Read(_store.Load() ?? new(), warn);

    public void Save(AppConfig config, IEnumerable<string> changedKeys, bool resetTextStyles = false)
    {
        var keys = changedKeys.ToHashSet();
        if (resetTextStyles)
        {
            keys.Add("textStyles");
        }
        if (keys.Count == 0)
        {
            return;
        }

        var stored = _store.Load() ?? new();
        foreach (var unknown in stored.Keys.Except(ConfigSets.Keys).ToArray())
        {
            stored.Remove(unknown);
        }
        var values = JsonSerializer.SerializeToElement(config, DayNoteJson.Options);
        foreach (var key in keys)
        {
            if (resetTextStyles && key == "textStyles" && ConfigSets.IsBuiltIn(config, key))
            {
                stored.Remove(key);
            }
            else
            {
                stored[key] = values.GetProperty(key);
            }
        }
        _store.Save(stored);
    }
}
