using System.Text.Json.Serialization;

using DayNote.Core.Time;

namespace DayNote.Core.Configuration;

/// <summary>
/// Durable user preferences, persisted to <c>~/.daynote/config.json</c>. Properties are declared
/// in a deliberate, grouped order so the serialized file is canonical: app appearance, then editor
/// appearance, then editing behavior, then display.
/// </summary>
public sealed class AppConfig : IJsonOnDeserialized
{
    /// <summary>The bundled default UI (chrome) font, registered via <c>.WithInterFont()</c>.</summary>
    public const string DefaultUiFontFamily = "Inter";

    /// <summary>
    /// The built-in text-style presets — the first-run seed, used by the <see cref="TextStyles"/>
    /// initializer below. Returns a fresh list of fresh presets on every call, so each caller owns a
    /// mutable copy it can edit without touching the built-ins.
    /// </summary>
    public static List<EditorTextStyle> DefaultTextStyles() => new()
    {
        new EditorTextStyle { IsDefault = true, FontFamily = EditorTextStyle.DefaultFixedWidthFamilies, FontSize = 14, LineSpacing = 1.4, Padding = 12 },
        new EditorTextStyle { FontFamily = "Inter", FontSize = 15, LineSpacing = 1.5, Padding = 14 },
    };

    // App appearance — the UI (chrome) font family. Family only; an empty value falls back to the
    // bundled default (Inter). Applied app-wide; the editor body uses its own text-style preset, so
    // this never touches the note content's font.
    public string UiFontFamily { get; set; } = DefaultUiFontFamily;

    // App appearance — the theme. System follows the OS; applied app-wide before the main window
    // exists and again on each Save.
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    // Editor appearance — the text-style presets, one of them flagged as the default. They seed from
    // the built-in defaults on first run; from then on they are the user's to edit.
    public List<EditorTextStyle> TextStyles { get; set; } = DefaultTextStyles();

    /// <summary>
    /// The preset an older config selected by name, before the default became a flag on the preset.
    /// Read only for that migration, and never written.
    /// </summary>
    [JsonInclude, JsonPropertyName("selectedTextStyle"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? LegacySelectedTextStyle { get; set; }

    // Editing behavior.
    public double AutosaveDelaySeconds { get; set; } = 2;

    // Display — the zone times are shown in: an IANA id chosen from the list, or System
    // (DayNoteTime.SystemZone), which follows the computer's zone at every launch.
    public string TimeZone { get; set; } = DayNoteTime.SystemZone;

    /// <summary>
    /// The zone an older config typed as an IANA id, before the zone was chosen from a list and before
    /// System existed. Read only for that migration, and never written.
    /// </summary>
    [JsonInclude, JsonPropertyName("displayTimeZone"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? LegacyDisplayTimeZone { get; set; }

    /// <summary>
    /// The zone every older config was seeded with, whether or not its user ever chose it. It carries
    /// no choice, so it migrates to System, which on a computer in that zone shows the same times.
    /// </summary>
    internal const string LegacyDefaultTimeZone = "Asia/Tokyo";

    /// <summary>The preset the editor uses: the flagged default, or the first when none is flagged.</summary>
    public EditorTextStyle? ResolveDefaultStyle() =>
        TextStyles.FirstOrDefault(style => style.IsDefault) ?? TextStyles.FirstOrDefault();

    /// <summary>
    /// Makes exactly one preset the default after a load. An older config names its selection, so the
    /// preset with that name becomes the default; the names themselves are then dropped.
    /// </summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        var chosen = TextStyles.FirstOrDefault(style => style.IsDefault)
            ?? TextStyles.FirstOrDefault(style =>
                string.Equals(style.LegacyName, LegacySelectedTextStyle, StringComparison.OrdinalIgnoreCase))
            ?? TextStyles.FirstOrDefault();
        foreach (var style in TextStyles)
        {
            style.IsDefault = ReferenceEquals(style, chosen);
            style.LegacyName = null;
        }

        LegacySelectedTextStyle = null;

        if (LegacyDisplayTimeZone is { } legacyZone && DayNoteTime.IsSystem(TimeZone))
        {
            TimeZone = MigrateLegacyTimeZone(legacyZone);
        }

        LegacyDisplayTimeZone = null;
    }

    /// <summary>
    /// The setting an older config's typed zone becomes: the seeded default, a blank, or an id no zone
    /// answers to follows the computer; any other zone the user typed stays chosen.
    /// </summary>
    internal static string MigrateLegacyTimeZone(string legacyZone)
    {
        var trimmed = legacyZone.Trim();
        return string.Equals(trimmed, LegacyDefaultTimeZone, StringComparison.Ordinal)
            || !DayNoteTime.TryResolveTimeZone(trimmed, out _)
            ? DayNoteTime.SystemZone
            : trimmed;
    }

    /// <summary>Returns a deep copy, used to give the settings dialog an editable working copy.</summary>
    public AppConfig Copy() => new()
    {
        UiFontFamily = UiFontFamily,
        Theme = Theme,
        TextStyles = TextStyles.Select(style => style.Copy()).ToList(),
        AutosaveDelaySeconds = AutosaveDelaySeconds,
        TimeZone = TimeZone,
    };
}
