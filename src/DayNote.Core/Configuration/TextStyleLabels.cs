using System.Globalization;

namespace DayNote.Core.Configuration;

/// <summary>The names text-style presets go by, in the settings list and in messages.</summary>
public static class TextStyleLabels
{
    public const string NoFontFamily = "No font family";

    /// <summary>
    /// Each preset's label: the first font family it names, as the user wrote it, with its size added
    /// when another preset would otherwise show the same label. The label follows the typing rather
    /// than the font the app resolves to, so a family the system does not have still shows the name
    /// it was given instead of the fallback's, and a half-typed name is visibly half-typed.
    /// </summary>
    public static IReadOnlyList<string> For(IReadOnlyList<EditorTextStyle> styles)
    {
        var families = styles.Select(style => FirstFamily(style.FontFamily)).ToList();
        return families
            .Select((family, index) =>
                families.Count(other => string.Equals(other, family, StringComparison.OrdinalIgnoreCase)) > 1
                    ? $"{family} {styles[index].FontSize.ToString("0.##", CultureInfo.InvariantCulture)}"
                    : family)
            .ToList();
    }

    private static string FirstFamily(string? value)
    {
        var first = (value ?? string.Empty).Split(',')[0].Trim();
        return first.Length == 0 ? NoFontFamily : first;
    }
}
