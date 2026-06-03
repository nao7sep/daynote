using System.Text;

namespace DayNote.Core.Text;

/// <summary>
/// Body normalization inherited from quickdeck, applied directly to the live body on read and
/// write. Line endings are normalized to LF, control characters disallowed by TOML strings
/// (anything other than tab and newline) are removed, and leading and trailing blank or
/// whitespace-only lines are stripped while interior blank lines are preserved.
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

        var stripped = NormalizeLineEndingsAndStripControls(body);
        return TrimOuterBlankLines(stripped);
    }

    private static string NormalizeLineEndingsAndStripControls(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\r':
                    // Collapse CRLF and lone CR into a single LF.
                    builder.Append('\n');
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    break;
                case '\n':
                case '\t':
                    builder.Append(c);
                    break;
                default:
                    // Drop control characters (U+0000–U+001F and U+007F) other than tab/newline,
                    // which TOML strings disallow. Everything else is kept verbatim.
                    if (c < ' ' || c == Delete)
                    {
                        break;
                    }
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string TrimOuterBlankLines(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var lines = text.Split('\n');

        var start = 0;
        while (start < lines.Length && lines[start].Trim().Length == 0)
        {
            start++;
        }

        var end = lines.Length;
        while (end > start && lines[end - 1].Trim().Length == 0)
        {
            end--;
        }

        if (start >= end)
        {
            return string.Empty;
        }

        return string.Join('\n', lines[start..end]);
    }
}
