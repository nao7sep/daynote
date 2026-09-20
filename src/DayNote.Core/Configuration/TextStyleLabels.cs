using System.Globalization;

namespace DayNote.Core.Configuration;

/// <summary>The names text-style presets go by, in the settings list and in messages.</summary>
public static class TextStyleLabels
{
    public const string NoFontFamily = "No font family";

    /// <summary>
    /// Each preset's label: the name of the font family it uses, with its size added when another
    /// preset would otherwise show the same label. <paramref name="familyName"/> turns a preset's
    /// free-text family into that name, since only the app layer knows which fonts are installed.
    /// </summary>
    public static IReadOnlyList<string> For(IReadOnlyList<EditorTextStyle> styles, Func<string, string> familyName)
    {
        var families = styles
            .Select(style => string.IsNullOrWhiteSpace(style.FontFamily) ? NoFontFamily : familyName(style.FontFamily))
            .ToList();
        return families
            .Select((family, index) =>
                families.Count(other => string.Equals(other, family, StringComparison.OrdinalIgnoreCase)) > 1
                    ? $"{family} {styles[index].FontSize.ToString("0.##", CultureInfo.InvariantCulture)}"
                    : family)
            .ToList();
    }
}
