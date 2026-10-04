using System;
using System.Collections.Generic;

using DayNote.Core.Time;

namespace DayNote.Core.Configuration;

/// <summary>One text-style editor row's values, decoupled from the Avalonia controls.</summary>
public sealed record TextStyleDraft(string FontFamily, double FontSize, double LineSpacing, double Padding);

/// <summary>The settings editor's whole working state as plain data, so its validity is testable.</summary>
public sealed record SettingsDraft(string TimeZone, double AutosaveSeconds, IReadOnlyList<TextStyleDraft> Styles, bool HasDefault);

/// <summary>
/// What a valid setting is: the settings dialog applies it at Save, and <see cref="ConfigSets"/> as
/// each set is read.
/// </summary>
public static class SettingsValidator
{
    public const double MinFontSize = 8;
    public const double MaxFontSize = 48;
    public const double MinLineSpacing = 1.0;
    public const double MaxLineSpacing = 3.0;
    public const double MinPadding = 0;
    public const double MaxPadding = 48;
    public const double MinAutosaveSeconds = 0.25;
    public const double MaxAutosaveSeconds = 60;

    public static bool IsValid(SettingsDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return IsTimeZoneSetting(draft.TimeZone)
            && IsAutosaveDelay(draft.AutosaveSeconds)
            && AreValidTextStyles(draft.Styles, draft.HasDefault);
    }

    /// <summary>Whether a language setting is exactly System or one of the offered tags, as Save writes it.</summary>
    public static bool IsLanguageSetting(string setting) =>
        setting == AppConfig.SystemLanguage || AppConfig.LanguageTags.Contains(setting);

    /// <summary>Whether a time-zone setting is System or a zone the platform knows.</summary>
    public static bool IsTimeZoneSetting(string setting) =>
        DayNoteTime.IsSystem(setting) || DayNoteTime.TryResolveTimeZone(setting.Trim(), out _);

    public static bool IsAutosaveDelay(double seconds) => InRange(seconds, MinAutosaveSeconds, MaxAutosaveSeconds);

    public static bool AreValidTextStyles(IReadOnlyList<EditorTextStyle> styles) => AreValidTextStyles(
        styles.Select(style => new TextStyleDraft(style.FontFamily, style.FontSize, style.LineSpacing, style.Padding)).ToList(),
        styles.Count(style => style.IsDefault) == 1);

    private static bool AreValidTextStyles(IReadOnlyList<TextStyleDraft> styles, bool hasDefault) =>
        styles.Count > 0
        && hasDefault
        && styles.All(style => !string.IsNullOrWhiteSpace(style.FontFamily)
            && InRange(style.FontSize, MinFontSize, MaxFontSize)
            && InRange(style.LineSpacing, MinLineSpacing, MaxLineSpacing)
            && InRange(style.Padding, MinPadding, MaxPadding));

    private static bool InRange(double value, double min, double max) => value >= min && value <= max;
}
