using System.Text;

namespace DayNote.Core.Text;

/// <summary>
/// Body normalization applied on every read and write. The whitespace cleanup is the shared
/// <see cref="TextCleanup.Multiline"/> (line endings to LF, per-line trailing whitespace trimmed,
/// outer blank lines dropped, indentation and interior blank lines preserved). On top of that — the
/// one body-specific concern the text-cleanup conventions deliberately leave out — control characters
/// that TOML strings disallow (everything below U+0020 except tab and newline, plus U+007F) are
/// stripped, so the in-memory body always equals the canonical stored form.
/// </summary>
public static class BodyCleanup
{
    private const char Delete = (char)0x7F;

    /// <summary>Returns the normalized body. Returns an empty string when nothing meaningful remains.</summary>
    public static string Normalize(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        // Preserve CR separators before stripping TOML-disallowed controls. Strip controls before
        // whitespace cleanup so newly exposed trailing whitespace and edge blanks are cleaned too.
        var normalizedLines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return TextCleanup.Multiline(StripDisallowedControls(normalizedLines));
    }

    private static string StripDisallowedControls(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if ((c < ' ' && c != '\t' && c != '\n') || c == Delete)
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
