using System.Text.Json;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>
/// Reads binders.json once and writes it from memory: the binders the user has opened, in the order the
/// user keeps them, each with its path and the title the user gave it. The list has its own file because
/// it changes on its own, with every open, rename, reorder and forget, apart from the settings
/// (data-lifecycle-conventions, Storage boundaries); the current binder and note are view state and stay
/// in state.json. Like config.json it is recorded in the backup history.
/// </summary>
/// <remarks>
/// A file that cannot be read, parsed or is newer throws as <see cref="JsonStore{T}"/> does, and the
/// caller stops untouched. A list that fails its check reads as none, with a warning, and is written back
/// as stored until the user changes the list, since a fallback must not silently replace what the user
/// built up (config-sets-conventions); keys this build does not know are written back too.
/// </remarks>
public sealed class BinderListStore(string path, Action<string> warn)
{
    public const string ListKey = "binders";

    private readonly JsonStore<Dictionary<string, JsonElement>> _store = new(path, FileFormats.BinderList);

    // The map the file holds as of the last load or save; null while there is no file.
    private Dictionary<string, JsonElement>? _onDisk;

    // A stored list that failed its check, written back until the user changes the list.
    private JsonElement? _kept;

    /// <summary>Reads the list; no file is an empty list.</summary>
    public List<KnownBinder> Load()
    {
        _onDisk = _store.Load();
        _kept = null;
        if (_onDisk is null || !_onDisk.TryGetValue(ListKey, out var stored))
            return [];

        if (!IsValid(stored))
        {
            warn(ListKey);
            _kept = stored.Clone();
            return [];
        }

        return stored.Deserialize<List<KnownBinder>>(DayNoteJson.Options)!;
    }

    /// <summary>Writes the list as the user now holds it; a list that would not change the file writes nothing.</summary>
    public void Save(IReadOnlyList<KnownBinder> binders)
    {
        var list = _kept is { } kept && binders.Count == 0
            ? kept
            : JsonSerializer.SerializeToElement(binders, DayNoteJson.Options);
        if (_onDisk is null
            ? binders.Count == 0
            : _onDisk.TryGetValue(ListKey, out var stored) && JsonElement.DeepEquals(stored, list))
        {
            return;
        }

        var next = new Dictionary<string, JsonElement>(_onDisk ?? new()) { [ListKey] = list };
        _store.Save(next);
        _onDisk = next;
        // Only a successful write of a list the user changed retires the kept one.
        if (binders.Count > 0)
            _kept = null;
    }

    // Each entry has a path and a title, and no two paths name the same file lexically. Identity checking
    // is lexical: an offline binder remains valid, and its authored spelling stays untouched; existing-file
    // aliases belong to the binder load boundary.
    private static bool IsValid(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return false;

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(path.GetString()))
                return false;

            try
            {
                if (!paths.Add(PathKey.Normalize(path.GetString()!)))
                    return false;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }
        return true;
    }
}
