namespace DayNote.Core.Configuration;

/// <summary>
/// Durable user preferences, persisted to <c>~/.daynote/config.json</c>. Properties are declared
/// in a deliberate, grouped order so the serialized file is canonical: app appearance, then editor
/// appearance, then editing behavior, then display.
/// </summary>
public sealed class AppConfig
{
    /// <summary>The bundled default UI (chrome) font, registered via <c>.WithInterFont()</c>.</summary>
    public const string DefaultUiFontFamily = "Inter";

    // App appearance — the UI (chrome) font family. Family only; an empty value falls back to the
    // bundled default (Inter). Applied app-wide; the editor body uses its own text-style preset, so
    // this never touches the note content's font.
    public string UiFontFamily { get; set; } = DefaultUiFontFamily;

    // Editor appearance — named text-style presets and the one currently selected (by name).
    public List<EditorTextStyle> TextStyles { get; set; } = new()
    {
        new EditorTextStyle { Name = "Mono", FontFamily = "Menlo", FontSize = 14, LineSpacing = 1.4, Padding = 12 },
        new EditorTextStyle { Name = "Sans", FontFamily = "Inter", FontSize = 15, LineSpacing = 1.5, Padding = 14 },
    };
    public string SelectedTextStyle { get; set; } = "Mono";

    // Editing behavior.
    public double AutosaveDelaySeconds { get; set; } = 2;

    // Display.
    public string DisplayTimeZone { get; set; } = "Asia/Tokyo";

    /// <summary>
    /// The active text-style preset: the one whose name matches <see cref="SelectedTextStyle"/>
    /// (case-insensitively), falling back to the first preset, or null when there are none.
    /// </summary>
    public EditorTextStyle? ResolveSelectedStyle() =>
        TextStyles.FirstOrDefault(s => string.Equals(s.Name, SelectedTextStyle, StringComparison.OrdinalIgnoreCase))
        ?? TextStyles.FirstOrDefault();

    /// <summary>Returns a deep copy, used to give the settings dialog an editable working copy.</summary>
    public AppConfig Copy() => new()
    {
        UiFontFamily = UiFontFamily,
        TextStyles = TextStyles.Select(style => style.Copy()).ToList(),
        SelectedTextStyle = SelectedTextStyle,
        AutosaveDelaySeconds = AutosaveDelaySeconds,
        DisplayTimeZone = DisplayTimeZone,
    };
}
