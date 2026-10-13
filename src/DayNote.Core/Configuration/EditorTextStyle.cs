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

    /// <summary>
    /// The size, spacing and padding defaults are round values (15, 1.5, 15) so a user tuning a style
    /// starts from numbers that are easy to reason about.
    /// </summary>
    public double FontSize { get; set; } = 15;

    /// <summary>Line height as a multiple of the font size (1.0 = single spacing); applied as size × this.</summary>
    public double LineSpacing { get; set; } = 1.5;

    /// <summary>Uniform padding, in device-independent pixels, inside the editor around the text.</summary>
    public double Padding { get; set; } = 15;

    public bool Bold { get; set; }

    public bool Italic { get; set; }

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
