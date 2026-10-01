using DayNote.Core.Time;

namespace DayNote.Core.Configuration;

/// <summary>
/// Durable user preferences, persisted to <c>~/.daynote/config.json</c>. Properties are declared
/// in a deliberate, grouped order so the serialized file is canonical: the interface language, then app
/// appearance, then editor appearance, then editing behavior, then display.
/// </summary>
public sealed class AppConfig
{
    /// <summary>The bundled default UI (chrome) font, registered via <c>.WithInterFont()</c>.</summary>
    public const string DefaultUiFontFamily = "Inter";

    /// <summary>
    /// The built-in text-style presets, used while the <see cref="TextStyles"/> set is absent. Returns a fresh list of fresh presets on every call, so each caller owns a
    /// mutable copy it can edit without touching the built-ins.
    /// </summary>
    public static List<EditorTextStyle> DefaultTextStyles() => new()
    {
        new EditorTextStyle { IsDefault = true, FontFamily = EditorTextStyle.DefaultFixedWidthFamilies, FontSize = 14, LineSpacing = 1.4, Padding = 12 },
        new EditorTextStyle { FontFamily = "Inter", FontSize = 15, LineSpacing = 1.5, Padding = 14 },
    };

    /// <summary>The saved language that means "follow the computer's own languages".</summary>
    public const string SystemLanguage = "system";

    /// <summary>
    /// The interface language: a BCP 47 tag from the set, or <see cref="SystemLanguage"/> to follow the
    /// computer's own languages at each launch. The app reads it straight out of the file before it is
    /// built (<c>I18n/LanguageBootstrap.cs</c>), so the first frame is already in it; a missing or
    /// unknown value means System.
    /// </summary>
    public string Language { get; set; } = SystemLanguage;

    // App appearance — the UI (chrome) font family. Family only; nothing is stored until the user
    // types one. Empty shows the bundled default (Inter) as the field's placeholder and resolves to
    // it. Applied app-wide; the editor body uses its own text-style preset, so this never touches the
    // note content's font.
    public string UiFontFamily { get; set; } = "";

    // App appearance — the theme. System follows the OS; applied app-wide before the main window
    // exists and again on each Save.
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    // Editor appearance — the text-style presets, one of them flagged as the default. The built-ins apply
    // until the user changes or resets this set.
    public List<EditorTextStyle> TextStyles { get; set; } = DefaultTextStyles();

    // Editing behavior.
    public double AutosaveDelaySeconds { get; set; } = 2;

    // Display — the zone times are shown in: an IANA id chosen from the list, or System
    // (DayNoteTime.SystemZone), which follows the computer's zone at every launch.
    public string TimeZone { get; set; } = DayNoteTime.SystemZone;

    /// <summary>The preset the editor uses: the flagged default, or the first when none is flagged.</summary>
    public EditorTextStyle? ResolveDefaultStyle() =>
        TextStyles.FirstOrDefault(style => style.IsDefault) ?? TextStyles.FirstOrDefault();

    /// <summary>Returns a deep copy, used to give the settings dialog an editable working copy.</summary>
    public AppConfig Copy() => new()
    {
        Language = Language,
        UiFontFamily = UiFontFamily,
        Theme = Theme,
        TextStyles = TextStyles.Select(style => style.Copy()).ToList(),
        AutosaveDelaySeconds = AutosaveDelaySeconds,
        TimeZone = TimeZone,
    };
}
