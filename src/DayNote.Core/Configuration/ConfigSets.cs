using System.Text.Json;

namespace DayNote.Core.Configuration;

/// <summary>Whole-set reading, checking and comparison, per the config-sets-conventions.</summary>
public static class ConfigSets
{
    // AppConfig declares the known keys and built-ins in one place.
    private static readonly JsonElement BuiltIns = JsonSerializer.SerializeToElement(new AppConfig(), DayNoteJson.Options);
    public static IEnumerable<string> Keys => BuiltIns.EnumerateObject().Select(property => property.Name);

    public static AppConfig Read(IReadOnlyDictionary<string, JsonElement> stored, Action<string> warn)
    {
        var effective = new Dictionary<string, JsonElement>();
        foreach (var key in Keys)
        {
            if (!stored.TryGetValue(key, out var value))
            {
                continue;
            }

            if (IsValid(key, value))
            {
                effective[key] = value;
            }
            else
            {
                warn(key);
            }
        }

        return JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(effective), DayNoteJson.Options)!;
    }

    /// <summary>The keys of the sets the draft holds differently from the baseline.</summary>
    public static IReadOnlyList<string> ChangedKeys(AppConfig draft, AppConfig baseline)
    {
        var draftValues = JsonSerializer.SerializeToElement(draft, DayNoteJson.Options);
        var baselineValues = JsonSerializer.SerializeToElement(baseline, DayNoteJson.Options);
        return Keys.Where(key => !JsonElement.DeepEquals(draftValues.GetProperty(key), baselineValues.GetProperty(key))).ToArray();
    }

    /// <summary>Every set that differs from its built-in, whole, keyed as the file stores it.</summary>
    public static Dictionary<string, JsonElement> UserSets(AppConfig config)
    {
        var values = JsonSerializer.SerializeToElement(config, DayNoteJson.Options);
        return Keys
            .Where(key => !JsonElement.DeepEquals(values.GetProperty(key), BuiltIns.GetProperty(key)))
            .ToDictionary(key => key, key => values.GetProperty(key));
    }

    // Reading and healing (config-sets-conventions), by the validator Settings applies at Save.
    private static bool IsValid(string key, JsonElement value) => key switch
    {
        "binders" => HasBinderListShape(value),
        "theme" => IsTheme(value),
        _ => HasShape(value, BuiltIns.GetProperty(key)) && key switch
        {
            "timeZone" => SettingsValidator.IsTimeZoneSetting(value.GetString()!),
            "autosaveDelaySeconds" => SettingsValidator.IsAutosaveDelay(value.GetDouble()),
            "textStyles" => SettingsValidator.AreValidTextStyles(value.Deserialize<List<EditorTextStyle>>(DayNoteJson.Options)!),
            _ => true,
        },
    };

    private static bool HasBinderListShape(JsonElement value) => value.ValueKind == JsonValueKind.Array
        && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
            && item.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String);

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
