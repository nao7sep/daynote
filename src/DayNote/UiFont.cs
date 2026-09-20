using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using DayNote.Core.Configuration;

namespace DayNote;

/// <summary>
/// Resolves the user's UI (chrome) font-family string to a concrete <see cref="FontFamily"/>.
/// Per the app-chrome-conventions' native-toolkit rule, the free-text value may be a comma-separated
/// list; this picks the first family actually installed and otherwise falls back to the bundled
/// default (Inter), so a misspelled or absent family never leaves the chrome unstyled.
/// </summary>
public static class UiFont
{
    /// <summary>
    /// The bundled Inter as the font manager reaches it. A bare "Inter" does NOT resolve
    /// to the embedded collection `.WithInterFont()` registers — with no system Inter
    /// installed it silently falls back to the platform default (Helvetica on macOS),
    /// whose ascent barely clears its cap height, so every label sits visibly high.
    /// The display name stays "Inter"; this URI is what actually loads it.
    /// </summary>
    public const string BundledUiFontUri = "fonts:Inter#Inter";

    /// <summary>Splits a comma-separated family string into trimmed, unquoted names.</summary>
    public static IEnumerable<string> ParseFamilies(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var part in value.Split(','))
        {
            var name = part.Trim().Trim('"', '\'').Trim();
            if (name.Length > 0)
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// The first requested family that is actually installed, or the bundled default when none match.
    /// </summary>
    public static FontFamily Resolve(string? value) =>
        FindFirst(value)?.Family ?? new FontFamily(BundledUiFontUri);

    /// <summary>
    /// The editor's family for a text-style preset: the first requested family actually installed,
    /// and when none is, the fixed-width default, so a preset naming a face one platform lacks (Menlo
    /// on Windows) still gets a fixed-width face rather than the platform's proportional one.
    /// </summary>
    public static FontFamily ResolveEditor(string? value) =>
        (FindFirst(value) ?? FindFirst(EditorTextStyle.DefaultFixedWidthFamilies))?.Family
        // Neither Menlo nor Consolas: the generic name, which fontconfig resolves on Linux.
        ?? new FontFamily("monospace");

    /// <summary>
    /// The name a text-style preset goes by: the family <see cref="ResolveEditor"/> uses, as the user
    /// wrote it, so a Japanese name stays Japanese and a list naming several families shows the one
    /// actually installed.
    /// </summary>
    public static string EditorFamilyName(string? value) =>
        (FindFirst(value) ?? FindFirst(EditorTextStyle.DefaultFixedWidthFamilies))?.Name ?? "monospace";

    private static (FontFamily Family, string Name)? FindFirst(string? value)
    {
        foreach (var name in ParseFamilies(value))
        {
            // Inter always means the bundled Inter. A system-reached "Inter" lacks the bundled weights,
            // and drawing bold text with it throws.
            if (string.Equals(name, AppConfig.DefaultUiFontFamily, StringComparison.OrdinalIgnoreCase))
            {
                return (new FontFamily(BundledUiFontUri), AppConfig.DefaultUiFontFamily);
            }

            if (FindInstalled(name) is { } installed)
            {
                return (installed, name);
            }
        }

        return null;
    }

    // A family name no font uses, whose lookup yields the platform's default face.
    private const string UnknownFamilyProbe = "DayNote Unknown Family Probe";

    private static FontFamily? FindInstalled(string name)
    {
        try
        {
            // Avalonia's own view of fonts is partial. Its font list grows with every name ever
            // requested, fallbacks included, so a name looked up once appears installed from then on;
            // and a face exposes its localized per-weight names (ヒラギノ角ゴシック W3) but not its
            // localized family name (ヒラギノ角ゴシック). The platform's matcher knows every name a font
            // declares in any language, and a name it does not know yields its default face. So a name
            // is installed when the matcher answers with any other face, or with the default face when
            // that face itself declares the name (Helvetica, say, typed on purpose).
            var fonts = FontManager.Current;
            var family = new FontFamily(name);
            if (!fonts.TryGetGlyphTypeface(new Typeface(family), out var face))
            {
                return null;
            }

            var isDefaultFace = fonts.TryGetGlyphTypeface(new Typeface(new FontFamily(UnknownFamilyProbe)), out var fallback)
                && string.Equals(face.FamilyName, fallback.FamilyName, StringComparison.Ordinal);
            return isDefaultFace && !Declares(face, name) ? null : family;
        }
        catch
        {
            // A font-manager hiccup must never crash settings; treat the family as absent so the
            // caller falls back to its default.
            return null;
        }
    }

    /// <summary>True when the face declares <paramref name="name"/> as a family name in any language.</summary>
    internal static bool Declares(GlyphTypeface face, string name) =>
        string.Equals(face.FamilyName, name, StringComparison.OrdinalIgnoreCase)
        || string.Equals(face.TypographicFamilyName, name, StringComparison.OrdinalIgnoreCase)
        || face.FamilyNames.Values.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase));
}
