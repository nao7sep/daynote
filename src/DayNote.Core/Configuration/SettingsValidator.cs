using System;
using System.Collections.Generic;
using System.Text.Json;

using DayNote.Core.Time;

namespace DayNote.Core.Configuration;

/// <summary>One text-style editor row's values, decoupled from the Avalonia controls.</summary>
public sealed record TextStyleDraft(string FontFamily, double FontSize, double LineSpacing, double Padding);

/// <summary>The settings editor's whole working state as plain data, so its validity is testable.</summary>
public sealed record SettingsDraft(string TimeZone, double AutosaveSeconds, IReadOnlyList<TextStyleDraft> Styles, bool HasDefault);

/// <summary>
/// The save-gating validation that used to live inside the settings dialog: timezone and
/// numeric-range checks, the non-blank font requirement, and the "at least one style, exactly
/// one default" invariant. Pure — the dialog projects its working copy into a
/// <see cref="SettingsDraft"/> and asks here.
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

        if (!IsTimeZoneSetting(draft.TimeZone)
            || !InRange(draft.AutosaveSeconds, MinAutosaveSeconds, MaxAutosaveSeconds))
        {
            return false;
        }

        if (draft.Styles.Count == 0 || !draft.HasDefault)
        {
            return false;
        }

        foreach (var style in draft.Styles)
        {
            if (string.IsNullOrWhiteSpace(style.FontFamily)
                || !InRange(style.FontSize, MinFontSize, MaxFontSize)
                || !InRange(style.LineSpacing, MinLineSpacing, MaxLineSpacing)
                || !InRange(style.Padding, MinPadding, MaxPadding))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a time-zone setting is System or a zone the platform knows.</summary>
    public static bool IsTimeZoneSetting(string setting) =>
        DayNoteTime.IsSystem(setting) || DayNoteTime.TryResolveTimeZone(setting.Trim(), out _);

    /// <summary>True when the working config differs from the saved original, by canonical JSON.</summary>
    public static bool IsDirty(AppConfig current, AppConfig original) =>
        JsonSerializer.Serialize(current, DayNoteJson.Options) != JsonSerializer.Serialize(original, DayNoteJson.Options);

    private static bool InRange(double value, double min, double max) => value >= min && value <= max;
}
