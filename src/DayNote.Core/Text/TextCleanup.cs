using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DayNote.Core.Text;

/// <summary>
/// DayNote's local realization of the fleet text-cleanup-conventions — the canonical C# snippets,
/// copied verbatim (there is no shared package). Three patterns: <see cref="SingleLine"/> for scalar
/// fields (titles, names), <see cref="Multiline"/> for bodies, and <see cref="Truncate"/> for a
/// single-line, grapheme-safe, minimum-length preview. Run these at commit/display time, never on a
/// keystroke (per the text-input-ime-conventions); Unicode (NFC) normalization is a separate concern
/// owned by that convention, not done here.
/// </summary>
public static partial class TextCleanup
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex LineBreakRun();

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex LineSplit();

    [GeneratedRegex(@"\s+$")]
    private static partial Regex TrailingWhitespace();

    /// <summary>
    /// Cleans a scalar value: always trims the ends. By default a whitespace run containing a line
    /// break collapses to one space (so a value pasted across lines becomes one line) while horizontal
    /// spacing is preserved; <paramref name="minify"/> instead collapses every whitespace run — including
    /// a lone full-width U+3000 — to one space.
    /// </summary>
    public static string SingleLine(string text, bool flattenLineBreaks = true, bool minify = false)
    {
        if (minify)
        {
            return WhitespaceRun().Replace(text, " ").Trim();
        }

        if (flattenLineBreaks)
        {
            return LineBreakRun().Replace(text, " ").Trim();
        }

        return text.Trim();
    }

    /// <summary>
    /// Cleans a multi-line body: normalizes line endings to LF and, by default, trims each line's
    /// trailing whitespace (<paramref name="trimLineEnds"/>) and drops blank lines before the first and
    /// after the last visible line (<paramref name="dropEdgeBlankLines"/>). Indentation and interior
    /// blank lines are preserved unless <paramref name="collapseBlankLines"/> reduces interior blank runs to one.
    /// </summary>
    public static string Multiline(
        string text,
        bool trimLineEnds = true,
        bool dropEdgeBlankLines = true,
        bool collapseBlankLines = false)
    {
        var lines = LineSplit().Split(text).ToList();
        if (trimLineEnds)
        {
            lines = lines.Select(l => TrailingWhitespace().Replace(l, "")).ToList();
        }

        int start = 0, end = lines.Count;
        if (dropEdgeBlankLines)
        {
            while (start < end && string.IsNullOrWhiteSpace(lines[start]))
            {
                start++;
            }

            while (end > start && string.IsNullOrWhiteSpace(lines[end - 1]))
            {
                end--;
            }
        }

        var outLines = new List<string>();
        var prevBlank = false;
        for (var i = start; i < end; i++)
        {
            var blank = string.IsNullOrWhiteSpace(lines[i]);
            if (collapseBlankLines && blank && prevBlank)
            {
                continue;
            }

            outLines.Add(lines[i]);
            prevBlank = blank;
        }

        return string.Join("\n", outLines);
    }

    /// <summary>
    /// A single-line snippet of <paramref name="text"/> of at least <paramref name="n"/> graphemes
    /// (a few over is fine), with whitespace runs — including newlines — collapsed to one space and
    /// leading/trailing whitespace dropped. <c>Truncated</c> is true only when a visible grapheme
    /// exists past the cut point, so an all-whitespace tail never reports a cut. No ellipsis is added.
    /// </summary>
    public static TruncateResult Truncate(string text, int n)
    {
        if (n <= 0)
        {
            return new TruncateResult(string.Empty, false);
        }

        var sb = new StringBuilder();
        var count = 0; // graphemes appended, including inserted spaces
        bool pendingSpace = false, budgetMet = false, truncated = false;

        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            var g = (string)e.Current;
            var isWhitespace = string.IsNullOrWhiteSpace(g);
            if (!budgetMet)
            {
                if (isWhitespace)
                {
                    if (count > 0)
                    {
                        pendingSpace = true; // skip leading, hold trailing
                    }

                    continue;
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                    count++;
                    pendingSpace = false;
                }

                sb.Append(g);
                count++;
                if (count >= n)
                {
                    budgetMet = true;
                }
            }
            else if (!isWhitespace)
            {
                truncated = true; // a visible grapheme exists past the cut point
                break;
            }
        }

        return new TruncateResult(sb.ToString(), truncated);
    }
}

/// <summary>The result of <see cref="TextCleanup.Truncate"/>: the snippet and whether content was cut.</summary>
public readonly record struct TruncateResult(string Text, bool Truncated);
