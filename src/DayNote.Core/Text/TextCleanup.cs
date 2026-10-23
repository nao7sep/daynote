using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DayNote.Core.Text;

/// <summary>
/// DayNote's local realization of the fleet text-cleanup-conventions, using the defaults needed by
/// its callers (there is no shared package). Three patterns: <see cref="SingleLine"/> for scalar
/// fields (titles, names), <see cref="Multiline"/> for bodies, and <see cref="Truncate"/> for a
/// single-line, grapheme-safe, minimum-length preview. Run these at commit/display time, never on a
/// keystroke (per the text-input-ime-conventions); Unicode (NFC) normalization is a separate concern
/// owned by that convention, not done here.
/// </summary>
public static partial class TextCleanup
{
    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex LineBreakRun();

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex LineSplit();

    [GeneratedRegex(@"\s+$")]
    private static partial Regex TrailingWhitespace();

    /// <summary>
    /// Cleans a scalar value: trims the ends and collapses a whitespace run containing a line break
    /// to one space, while preserving horizontal spacing.
    /// </summary>
    public static string SingleLine(string text) => LineBreakRun().Replace(text, " ").Trim();

    /// <summary>
    /// Cleans a multi-line body: normalizes line endings to LF, trims each line's trailing whitespace,
    /// and drops edge blank lines. Indentation and interior blank lines are preserved.
    /// </summary>
    public static string Multiline(string text)
    {
        var lines = LineSplit().Split(text).Select(l => TrailingWhitespace().Replace(l, "")).ToList();
        int start = 0, end = lines.Count;
        while (start < end && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        while (end > start && string.IsNullOrWhiteSpace(lines[end - 1]))
        {
            end--;
        }

        return string.Join("\n", lines.GetRange(start, end - start));
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
