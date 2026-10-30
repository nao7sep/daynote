using System.Text.Json;

namespace DayNote.Core.Configuration;

/// <summary>Whole-set reading, checking and comparison, per the config-sets-conventions.</summary>
public static class ConfigSets
{
    // AppConfig declares the known keys and built-ins in one place.
    private static readonly JsonElement BuiltIns = JsonSerializer.SerializeToElement(new AppConfig(), DayNoteJson.Options);
    public static IEnumerable<string> Keys => BuiltIns.EnumerateObject().Select(property => property.Name);

    // The set the user builds up by hand: the text-style presets. One that fails its check still reads as
    // its built-in, but its stored value is kept on disk until the user changes the set, since a fallback
    // must not silently replace authored choices (config-sets-conventions). The other sets are
    // presentation preferences whose built-in is a harmless fallback, normalized by the next save.
    private static readonly HashSet<string> Authored = ["textStyles"];

    /// <summary>
    /// The binder list config.json held before it had its own file, binders.json. It is retired: read
    /// once to move it, never kept, so the next save leaves it out.
    /// </summary>
    public const string RetiredBindersKey = "binders";

    /// <summary>Validates each stored set once, returning its effective values and entries to preserve.</summary>
    public static (AppConfig Config, Dictionary<string, JsonElement> Kept) Load(
        IReadOnlyDictionary<string, JsonElement> stored, Action<string> warn)
    {
        var effective = new Dictionary<string, JsonElement>();
        var kept = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in stored)
        {
            if (key == RetiredBindersKey)
                continue;

            if (!BuiltIns.TryGetProperty(key, out _))
            {
                kept[key] = value.Clone();
                continue;
            }

            if (IsValid(key, value))
            {
                // A language reads as its canonical tag, so Settings shows it and the next save writes it.
                effective[key] = key == "language"
                    ? JsonSerializer.SerializeToElement(SettingsValidator.CanonicalLanguage(value.GetString()!))
                    : value;
            }
            else
            {
                warn(key);
                if (Authored.Contains(key))
                    kept[key] = value.Clone();
            }
        }

        return (JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(effective), DayNoteJson.Options)!, kept);
    }

    /// <summary>The keys of the sets the draft holds differently from the baseline.</summary>
    public static IReadOnlyList<string> ChangedKeys(AppConfig draft, AppConfig baseline)
    {
        var draftValues = JsonSerializer.SerializeToElement(draft, DayNoteJson.Options);
        var baselineValues = JsonSerializer.SerializeToElement(baseline, DayNoteJson.Options);
        return Keys.Where(key => !JsonElement.DeepEquals(draftValues.GetProperty(key), baselineValues.GetProperty(key))).ToArray();
    }

    /// <summary>Every set that differs from its built-in, whole, keyed as the file stores it.</summary>
    public static Dictionary<string, JsonElement> UserSets(JsonElement values)
    {
        return Keys
            .Where(key => !JsonElement.DeepEquals(values.GetProperty(key), BuiltIns.GetProperty(key)))
            .ToDictionary(key => key, key => values.GetProperty(key));
    }

    // Reading and healing (config-sets-conventions), by the validator Settings applies at Save.
    private static bool IsValid(string key, JsonElement value) => key switch
    {
        "theme" => IsTheme(value),
        _ => HasShape(value, BuiltIns.GetProperty(key)) && key switch
        {
            "language" => SettingsValidator.CanonicalLanguage(value.GetString()!) is not null,
            "timeZone" => SettingsValidator.IsTimeZoneSetting(value.GetString()!),
            "autosaveDelaySeconds" => SettingsValidator.IsAutosaveDelay(value.GetDouble()),
            "textStyles" => SettingsValidator.AreValidTextStyles(value.Deserialize<List<EditorTextStyle>>(DayNoteJson.Options)!),
            _ => true,
        },
    };

    private static bool IsTheme(JsonElement value) => value.ValueKind == JsonValueKind.String
        && ThemePreferenceJsonConverter.TryParse(value.GetString(), out _);

    private static bool HasShape(JsonElement value, JsonElement expected) => expected.ValueKind switch
    {
        JsonValueKind.Object => value.ValueKind == JsonValueKind.Object
            && expected.EnumerateObject().All(property => value.TryGetProperty(property.Name, out var member)
                && HasShape(member, property.Value)),
        JsonValueKind.Array => value.ValueKind == JsonValueKind.Array
            && value.EnumerateArray().All(item => HasShape(item, expected[0])),
        JsonValueKind.True or JsonValueKind.False => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        JsonValueKind.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number),
        _ => value.ValueKind == expected.ValueKind,
    };
}
