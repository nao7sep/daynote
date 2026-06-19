namespace DayNote.Core.Configuration;

/// <summary>
/// Durable user preferences, persisted to <c>~/.daynote/config.json</c>. Properties are declared
/// in a deliberate, grouped order so the serialized file is canonical: editor appearance, then
/// editing behavior, then display.
/// </summary>
public sealed class AppConfig
{
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

    /// <summary>Returns a deep copy, used to give the settings dialog an editable working copy.</summary>
    public AppConfig Copy() => new()
    {
        TextStyles = TextStyles.Select(style => style.Copy()).ToList(),
        SelectedTextStyle = SelectedTextStyle,
        AutosaveDelaySeconds = AutosaveDelaySeconds,
        DisplayTimeZone = DisplayTimeZone,
    };
}
