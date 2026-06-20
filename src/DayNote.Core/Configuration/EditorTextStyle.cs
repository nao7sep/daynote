namespace DayNote.Core.Configuration;

/// <summary>
/// A named editor text-style preset, chosen by <see cref="Name"/>. It bundles the full set of
/// typographic settings applied to the note body: family, size, line spacing, padding, and weight/
/// slant. The binder's stored text never carries styling — a preset is a view-only preference for
/// how the plain-text body is rendered, so switching presets never changes saved content.
/// </summary>
public sealed class EditorTextStyle
{
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Font family name (e.g. <c>Menlo</c>, <c>Inter</c>). A concrete family is used rather than a CSS
    /// generic like <c>monospace</c>, which Avalonia's font manager does not resolve on every platform.
    /// </summary>
    public string FontFamily { get; set; } = "Menlo";

    public double FontSize { get; set; } = 14;

    /// <summary>Line height as a multiple of the font size (1.0 = single spacing); applied as size × this.</summary>
    public double LineSpacing { get; set; } = 1.4;

    /// <summary>Uniform padding, in device-independent pixels, inside the editor around the text.</summary>
    public double Padding { get; set; } = 12;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public EditorTextStyle Copy() => new()
    {
        Name = Name,
        FontFamily = FontFamily,
        FontSize = FontSize,
        LineSpacing = LineSpacing,
        Padding = Padding,
        Bold = Bold,
        Italic = Italic,
    };
}
