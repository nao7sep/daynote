using System;
using System.Collections.Generic;
using System.Linq;
using DayNote.Core.Storage;

namespace DayNote.ViewModels;

/// <summary>
/// UI-owned binder-name snapshots. Resolution remains at the background storage edge; revisions
/// prevent an older lookup from replacing the names adopted by a save, reload or newer lookup.
/// Registry rows retain their literal paths and do not use this document equality for mutations.
/// </summary>
internal sealed class BinderPathKeys
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);
    public int Revision { get; private set; }

    public bool Equal(string a, string b) => PathKey.Equal(Key(a), Key(b));

    private string Key(string path)
    {
        var full = PathKey.Normalize(path);
        return _keys.GetValueOrDefault(full, full);
    }

    public bool TryAdopt(int expectedRevision, IReadOnlyList<string> paths, IReadOnlyList<string?> keys)
    {
        if (expectedRevision != Revision)
            return false;
        for (var i = 0; i < paths.Count; i++)
            if (keys[i] is { } key)
                _keys[PathKey.Normalize(paths[i])] = key;
        Revision++;
        return true;
    }

    public void Published(string path, string key)
    {
        var oldKey = Key(path);
        foreach (var alias in _keys.Where(entry => entry.Value == oldKey).Select(entry => entry.Key).ToArray())
            _keys[alias] = key;
        _keys[PathKey.Normalize(path)] = key;
        Revision++;
    }
}
