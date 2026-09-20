using System.Text.Json.Serialization;

namespace DayNote.Core.Configuration;

/// <summary>
/// An editor text-style preset. It bundles the full set of typographic settings applied to the note
/// body: family, size, line spacing, padding, and weight/slant. A preset has no name of its own; it
/// goes by its font family. The binder's stored text never carries styling — a preset is a view-only
/// preference for how the plain-text body is rendered, so switching presets never changes saved content.
/// </summary>
public sealed class EditorTextStyle
{
    /// <summary>Whether this is the preset the editor uses; exactly one preset in a config is.</summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// The fixed-width default (app-chrome-conventions): Menlo on macOS, Consolas on Windows, and the
    /// generic <c>monospace</c> last, which Avalonia's font manager resolves only through fontconfig.
    /// </summary>
    public const string DefaultFixedWidthFamilies = "Menlo, Consolas, monospace";

    /// <summary>
    /// Font family, free text and possibly a comma-separated list (e.g. <c>Menlo, Consolas</c>, <c>Inter</c>);
    /// the app resolves it to the first installed family.
    /// </summary>
    public string FontFamily { get; set; } = DefaultFixedWidthFamilies;

    public double FontSize { get; set; } = 14;

    /// <summary>Line height as a multiple of the font size (1.0 = single spacing); applied as size × this.</summary>
    public double LineSpacing { get; set; } = 1.4;

    /// <summary>Uniform padding, in device-independent pixels, inside the editor around the text.</summary>
    public double Padding { get; set; } = 12;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    /// <summary>
    /// The name presets carried before they went by their font family. It is read only so that
    /// <see cref="AppConfig"/> can find the preset an older config selected by name, and never written.
    /// </summary>
    [JsonInclude, JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? LegacyName { get; set; }

    public EditorTextStyle Copy() => new()
    {
        IsDefault = IsDefault,
        FontFamily = FontFamily,
        FontSize = FontSize,
        LineSpacing = LineSpacing,
        Padding = Padding,
        Bold = Bold,
        Italic = Italic,
    };
}
