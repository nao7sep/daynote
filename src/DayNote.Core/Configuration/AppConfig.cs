using DayNote.Core.Time;

namespace DayNote.Core.Configuration;

/// <summary>
/// The user's settings, one property per set of <c>config.json</c> (config-sets-conventions), each
/// initialized to its built-in. The declaration order is the file's key order: the interface language,
/// app appearance, editor appearance, editing behavior, display, then the binder list.
/// </summary>
public sealed class AppConfig
{
    /// <summary>The bundled default UI (chrome) font, registered via <c>.WithInterFont()</c>.</summary>
    public const string DefaultUiFontFamily = "Inter";

    /// <summary>
    /// The built-in text-style presets. Returns a fresh list of fresh presets on every call, so each
    /// caller owns a mutable copy it can edit without touching the built-ins.
    /// </summary>
    public static List<EditorTextStyle> DefaultTextStyles() => new()
    {
        new EditorTextStyle { IsDefault = true, FontFamily = EditorTextStyle.DefaultFixedWidthFamilies, FontSize = 14, LineSpacing = 1.4, Padding = 12 },
        new EditorTextStyle { FontFamily = "Inter", FontSize = 15, LineSpacing = 1.5, Padding = 14 },
    };

    /// <summary>The saved language that means "follow the computer's own languages".</summary>
    public const string SystemLanguage = "system";

    /// <summary>
    /// The BCP 47 tags of the interface languages, in the picker's order: the Latin-script languages
    /// alphabetically by their own names, then Cyrillic, then Chinese, Japanese and Korean. The names
    /// they are shown under belong to the interface (<c>I18n/Languages.cs</c>).
    /// </summary>
    public static IReadOnlyList<string> LanguageTags { get; } = ["de", "en", "es", "fr", "it", "pt-BR", "ru", "zh-Hans", "ja", "ko"];

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

    /// <summary>The opened binders and their user-authored display titles.</summary>
    public List<KnownBinder> Binders { get; set; } = new();

    /// <summary>The preset the editor uses: the flagged default, or the first when none is flagged.</summary>
    public EditorTextStyle? ResolveDefaultStyle() =>
        TextStyles.FirstOrDefault(style => style.IsDefault) ?? TextStyles.FirstOrDefault();

    /// <summary>Returns a deep copy.</summary>
    public AppConfig Copy()
    {
        var copy = CopySettings();
        copy.Binders = Binders.Select(binder => new KnownBinder { Path = binder.Path, Title = binder.Title }).ToList();
        return copy;
    }

    /// <summary>Returns a deep copy of the sets the Settings dialog edits, without the binder list.</summary>
    public AppConfig CopySettings()
    {
        var copy = new AppConfig();
        copy.TakeSettings(this);
        return copy;
    }

    /// <summary>Takes a deep copy of the sets the Settings dialog edits, keeping this binder list.</summary>
    public void TakeSettings(AppConfig settings)
    {
        Language = settings.Language;
        UiFontFamily = settings.UiFontFamily;
        Theme = settings.Theme;
        TextStyles = settings.TextStyles.Select(style => style.Copy()).ToList();
        AutosaveDelaySeconds = settings.AutosaveDelaySeconds;
        TimeZone = settings.TimeZone;
    }
}
