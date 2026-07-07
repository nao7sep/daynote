using System;
using System.Collections.Generic;
using System.Text.Json;

using DayNote.Core.Time;

namespace DayNote.Core.Configuration;

/// <summary>One text-style editor row's values, decoupled from the Avalonia controls.</summary>
public sealed record TextStyleDraft(string Name, string FontFamily, double FontSize, double LineSpacing, double Padding);

/// <summary>The settings editor's whole working state as plain data, so its validity is testable.</summary>
public sealed record SettingsDraft(string TimeZone, double AutosaveSeconds, IReadOnlyList<TextStyleDraft> Styles, bool HasDefault);

/// <summary>
/// The save-gating validation that used to live inside the settings dialog: timezone and
/// numeric-range checks, the non-blank-name/font requirement, case-insensitive name
/// uniqueness, and the "at least one style, exactly one default" invariant. Pure — the
/// dialog projects its controls into a <see cref="SettingsDraft"/> and asks here.
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

        if (!DayNoteTime.TryResolveTimeZone(draft.TimeZone.Trim(), out _)
            || !InRange(draft.AutosaveSeconds, MinAutosaveSeconds, MaxAutosaveSeconds))
        {
            return false;
        }

        if (draft.Styles.Count == 0 || !draft.HasDefault)
        {
            return false;
        }

        // Compare names after trimming, so the blank check and the uniqueness check key off
        // the SAME canonical name (the trimmed value the model and UniqueName use) — the
        // earlier inconsistency was the blank check reading the raw textbox while the dedup
        // read the trimmed model name.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var style in draft.Styles)
        {
            var name = style.Name.Trim();
            if (name.Length == 0
                || string.IsNullOrWhiteSpace(style.FontFamily)
                || !InRange(style.FontSize, MinFontSize, MaxFontSize)
                || !InRange(style.LineSpacing, MinLineSpacing, MaxLineSpacing)
                || !InRange(style.Padding, MinPadding, MaxPadding)
                || !names.Add(name))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A name not already taken (case-insensitively) among <paramref name="existingNames"/>:
    /// the base name if free, else "<c>{base} 2</c>", "<c>{base} 3</c>", … The dialog uses this
    /// when adding or duplicating a style.
    /// </summary>
    public static string UniqueName(string baseName, IEnumerable<string> existingNames)
    {
        var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        var counter = 2;
        while (existing.Contains(name))
        {
            name = $"{baseName} {counter++}";
        }

        return name;
    }

    /// <summary>True when the working config differs from the saved original, by canonical JSON.</summary>
    public static bool IsDirty(AppConfig current, AppConfig original) =>
        JsonSerializer.Serialize(current, DayNoteJson.Options) != JsonSerializer.Serialize(original, DayNoteJson.Options);

    private static bool InRange(double value, double min, double max) => value >= min && value <= max;
}
