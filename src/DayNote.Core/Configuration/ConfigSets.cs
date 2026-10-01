using System.Text.Json;

namespace DayNote.Core.Configuration;

/// <summary>Whole-set shape checks and effective values, without normalizing user copies.</summary>
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

            if ((key == "binders" ? HasBinderListShape(value) : HasShape(value, BuiltIns.GetProperty(key)))
                && (key != "theme" || IsTheme(value)))
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

    public static IEnumerable<string> ChangedKeys(AppConfig current, AppConfig original)
    {
        var draft = JsonSerializer.SerializeToElement(current, DayNoteJson.Options);
        var baseline = JsonSerializer.SerializeToElement(original, DayNoteJson.Options);
        return Keys.Where(key => !JsonElement.DeepEquals(draft.GetProperty(key), baseline.GetProperty(key))).ToArray();
    }

    public static bool IsBuiltIn(AppConfig config, string key) => JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(config, DayNoteJson.Options).GetProperty(key), BuiltIns.GetProperty(key));

    private static bool HasBinderListShape(JsonElement value) => value.ValueKind == JsonValueKind.Array
        && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
            && item.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String);

    private static bool IsTheme(JsonElement value) => value.ValueKind == JsonValueKind.String
        && Enum.TryParse<ThemePreference>(value.GetString(), true, out var theme)
        && Enum.IsDefined(theme) && !int.TryParse(value.GetString(), out _);

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
