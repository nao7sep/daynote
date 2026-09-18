using System.Text.Json;
using System.Text.Json.Serialization;

namespace DayNote.Core.Configuration;

/// <summary>The saved appearance choice (app-chrome conventions, Theme). System follows the OS.</summary>
[JsonConverter(typeof(ThemePreferenceJsonConverter))]
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Writes the theme as a lowercase name and reads any name case-insensitively. A missing,
/// unrecognized, or wrongly typed value reads as System rather than failing the whole
/// config.json, so a file from before the setting, or from a newer build, loads untouched.
/// </summary>
public sealed class ThemePreferenceJsonConverter : JsonConverter<ThemePreference>
{
    public override ThemePreference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<ThemePreference>(reader.GetString(), ignoreCase: true, out var value)
            && Enum.IsDefined(value)
            && !int.TryParse(reader.GetString(), out _))
        {
            return value;
        }

        reader.Skip();
        return ThemePreference.System;
    }

    public override void Write(Utf8JsonWriter writer, ThemePreference value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString().ToLowerInvariant());
}
