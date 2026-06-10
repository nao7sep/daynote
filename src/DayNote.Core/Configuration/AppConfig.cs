namespace DayNote.Core.Configuration;

/// <summary>
/// Durable user preferences, persisted to <c>~/.daynote/config.json</c>. Properties are declared
/// in a deliberate, grouped order so the serialized file is canonical: editor appearance, then
/// editing behavior, then display, then backups.
/// </summary>
public sealed class AppConfig
{
    // Editor appearance.
    public List<string> EditorFonts { get; set; } = new() { "Inter" };
    public string EditorFont { get; set; } = "Inter";
    public double EditorFontSize { get; set; } = 14;

    // Editing behavior.
    public double AutosaveDelaySeconds { get; set; } = 1.5;

    // Display.
    public string DisplayTimeZone { get; set; } = "Asia/Tokyo";

    // Backups.
    public int BackupThrottleSeconds { get; set; } = 300;

    /// <summary>Returns a deep copy, used to give the settings dialog an editable working copy.</summary>
    public AppConfig Copy() => new()
    {
        EditorFonts = new List<string>(EditorFonts),
        EditorFont = EditorFont,
        EditorFontSize = EditorFontSize,
        AutosaveDelaySeconds = AutosaveDelaySeconds,
        DisplayTimeZone = DisplayTimeZone,
        BackupThrottleSeconds = BackupThrottleSeconds,
    };
}
