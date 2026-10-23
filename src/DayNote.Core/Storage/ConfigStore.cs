using System.Runtime.ExceptionServices;
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

    // Bootstrap and startup share the same validated outcome, including an original read failure.
    private readonly object _loadGate = new();
    private bool _loadAttempted;
    private AppConfig? _loadedConfig;
    private ExceptionDispatchInfo? _loadFailure;

    public AppConfig Load()
    {
        lock (_loadGate)
        {
            if (_loadAttempted)
            {
                _loadFailure?.Throw();
                return _loadedConfig!;
            }

            _loadAttempted = true;
            try
            {
                return _loadedConfig = LoadCore();
            }
            catch (Exception ex)
            {
                _loadFailure = ExceptionDispatchInfo.Capture(ex);
                throw;
            }
        }
    }

    private AppConfig LoadCore()
    {
        _onDisk = _store.Load();
        var stored = _onDisk ?? new();
        var (config, kept) = ConfigSets.Load(stored, warn);
        _kept = kept;
        _loaded = JsonSerializer.SerializeToElement(config, DayNoteJson.Options);
        return config;
    }

    public void Save(AppConfig config)
    {
        var values = JsonSerializer.SerializeToElement(config, DayNoteJson.Options);
        var sets = ConfigSets.UserSets(values);
        var replaced = new List<string>();
        foreach (var (key, stored) in _kept)
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
                // Retain the original until publication succeeds: a rejected edit may be
                // reverted before an unrelated setting is saved.
                replaced.Add(key);
            }
        }

        if (!_store.HasPendingPublication && (_onDisk is null ? sets.Count == 0 : SameSets(_onDisk, sets)))
        {
            return;
        }

        _store.Save(sets);
        _onDisk = sets;
        foreach (var key in replaced)
            _kept.Remove(key);
    }

    private static bool SameSets(Dictionary<string, JsonElement> stored, Dictionary<string, JsonElement> sets) =>
        stored.Count == sets.Count
        && sets.All(set => stored.TryGetValue(set.Key, out var value) && JsonElement.DeepEquals(value, set.Value));
}
