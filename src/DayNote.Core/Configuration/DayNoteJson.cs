using System.Text.Encodings.Web;
using System.Text.Json;

namespace DayNote.Core.Configuration;

/// <summary>
/// Shared JSON settings for the configuration and state files: pretty-printed, LF line endings,
/// camelCase keys, relaxed escaping so paths and non-ASCII text stay human-readable. Property
/// declaration order is preserved, which is how the grouped, canonical key order is achieved.
/// </summary>
public static class DayNoteJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
