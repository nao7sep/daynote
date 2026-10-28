using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DayNote.Core.Storage;

namespace DayNote.ViewModels;

/// <summary>
/// UI-owned binder-name snapshots. Resolution remains at the background storage edge; a save or reload
/// that adopts a binder's stored name cancels <see cref="Lookups"/>, so a lookup started before it cannot
/// put back the names it replaced. Registry rows retain their literal paths and do not use this document
/// equality for mutations.
/// </summary>
internal sealed class BinderPathKeys
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);
    private CancellationTokenSource _lookups = new();

    /// <summary>The token a lookup starting now carries to <see cref="Adopt"/>.</summary>
    public CancellationToken Lookups => _lookups.Token;

    public bool Equal(string a, string b) => PathKey.Equal(Key(a), Key(b));

    private string Key(string path)
    {
        var full = PathKey.Normalize(path);
        return _keys.GetValueOrDefault(full, full);
    }

    /// <summary>Adopts a lookup's names, unless a published name has replaced them since it started.</summary>
    public void Adopt(CancellationToken lookup, IReadOnlyList<string> paths, IReadOnlyList<string?> keys)
    {
        if (lookup.IsCancellationRequested)
            return;
        for (var i = 0; i < paths.Count; i++)
            if (keys[i] is { } key)
                _keys[PathKey.Normalize(paths[i])] = key;
    }

    public void Published(string path, string key)
    {
        var oldKey = Key(path);
        foreach (var alias in _keys.Where(entry => entry.Value == oldKey).Select(entry => entry.Key).ToArray())
            _keys[alias] = key;
        _keys[PathKey.Normalize(path)] = key;
        _lookups.Cancel();
        _lookups.Dispose();
        _lookups = new CancellationTokenSource();
    }
}
