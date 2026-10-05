using System.Text.Json;
using System.Text.Json.Nodes;
using DayNote.Core.Configuration;

namespace DayNote.Core.Storage;

/// <summary>
/// A typed JSON store for one file, the configuration or the state, per the store-recovery-conventions.
/// The file's top-level <c>formatVersion</c> is this store's own, and every save writes it first. A file
/// that cannot be parsed, records no format version, or does not fit the shape, is set aside under its
/// <c>.invalid</c> name and the load returns <c>null</c>; a failed move throws, so the next save never
/// writes over the bytes. A read error is not a parse failure and throws. A file recording a newer format
/// throws <see cref="NewerFormatException"/>, and is then never written by this store, whose saves do
/// nothing for the rest of the session. Writes are atomic and end with a trailing newline.
/// </summary>
public sealed class JsonStore<T>
    where T : class
{
    private const string FormatVersionKey = "formatVersion";

    private readonly string _path;
    private readonly int _formatVersion;
    private readonly bool _recordBackup;

    // Set when the load found a newer format; the file then belongs to the build that wrote it.
    private bool _newerOnDisk;

    public JsonStore(string path, int formatVersion, bool recordBackup = true)
    {
        _path = path;
        _formatVersion = formatVersion;
        _recordBackup = recordBackup;
    }

    /// <exception cref="NewerFormatException">The file records a newer format than this store reads.</exception>
    public T? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var json = File.ReadAllText(_path);
        JsonObject root;
        long version;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("The file is not a JSON object.");
            version = ReadFormatVersion(root);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // ArgumentException: JsonObject refuses a repeated key, which DayNote never writes.
            return Quarantine();
        }

        if (version > _formatVersion)
        {
            _newerOnDisk = true;
            throw new NewerFormatException(Path.GetFileName(_path), version, _formatVersion);
        }

        root.Remove(FormatVersionKey);
        try
        {
            return root.Deserialize<T>(DayNoteJson.Options);
        }
        catch (JsonException)
        {
            return Quarantine();
        }
    }

    public void Save(T value)
    {
        if (_newerOnDisk)
        {
            return;
        }

        var body = JsonSerializer.SerializeToNode(value, DayNoteJson.Options) as JsonObject
            ?? throw new InvalidOperationException($"{typeof(T).Name} does not serialize to a JSON object.");
        var members = body.ToList();
        body.Clear();
        var root = new JsonObject { [FormatVersionKey] = _formatVersion };
        foreach (var (key, member) in members)
        {
            root[key] = member;
        }

        var json = root.ToJsonString(DayNoteJson.Options);
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }

        AtomicFile.WriteAllText(_path, json, _recordBackup);
    }

    private static long ReadFormatVersion(JsonObject root)
    {
        return root.TryGetPropertyValue(FormatVersionKey, out var node)
            && node is JsonValue value && value.TryGetValue<long>(out var version) && version >= 1
            ? version
            : throw new JsonException($"{FormatVersionKey} is missing or not a positive integer.");
    }

    private T? Quarantine()
    {
        var quarantinePath = Path.Combine(
            Path.GetDirectoryName(_path) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(_path)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-utc.invalid");
        File.Move(_path, quarantinePath);
        QuarantineJournal.Record(quarantinePath);
        return null;
    }
}
