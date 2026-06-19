using System.Text;
using System.Text.RegularExpressions;

namespace DayNote.Core.Text;

/// <summary>
/// A direct implementation of the documented twitter-text weighted-length configuration (v3
/// defaults). Most characters weigh 2; characters in the Latin and general-punctuation ranges
/// below weigh 1; the budget is 280. URLs are counted at a fixed transformed length of 23.
///
/// Like twitter-text, the input is NFC-normalized before counting, so a decomposed accent weighs the
/// same as its single precomposed code point.
///
/// This is intentionally not the full twitter-text engine: emoji are not collapsed into single
/// units, and URL detection is limited to tokens that begin with a scheme or <c>www.</c> rather
/// than the exhaustive TLD-based matcher (in particular it does not reject over-long domain labels).
/// It is accurate for ordinary prose and good enough for drafting short-form posts.
/// </summary>
public static partial class TwitterText
{
    public const int MaxWeightedLength = 280;

    private const int Scale = 100;
    private const int DefaultWeight = 200;
    private const int LightWeight = 100;
    private const int TransformedUrlWeight = 23 * Scale;

    // Inclusive code-point ranges that weigh 1 (LightWeight). Everything else weighs 2.
    private static readonly (int Start, int End)[] LightRanges =
    {
        (0x0000, 0x10FF),
        (0x2000, 0x200D),
        (0x2010, 0x201F),
        (0x2032, 0x2037),
    };

    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    /// <summary>Returns the weighted character length of <paramref name="text"/>.</summary>
    public static int WeightedLength(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        text = Normalize(text);
        var urls = UrlRegex().Matches(text);
        var urlIndex = 0;
        var totalWeight = 0;
        var i = 0;

        while (i < text.Length)
        {
            if (urlIndex < urls.Count && i == urls[urlIndex].Index)
            {
                totalWeight += TransformedUrlWeight;
                i += urls[urlIndex].Length;
                urlIndex++;
                continue;
            }

            Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var consumed);
            totalWeight += WeightOf(rune.Value);
            i += consumed;
        }

        // All weights are multiples of Scale, so this division is exact.
        return totalWeight / Scale;
    }

    /// <summary>
    /// Returns the NFC-normalized form, the basis twitter-text counts. The common case (already-NFC
    /// text such as ASCII or CJK) is returned unchanged without allocating. Malformed UTF-16 — an
    /// unpaired surrogate — has no defined normal form; rather than throw on the live editor's text,
    /// it is counted as-is, matching the rest of this counter's never-throw behavior.
    /// </summary>
    private static string Normalize(string text)
    {
        try
        {
            return text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static int WeightOf(int codePoint)
    {
        foreach (var (start, end) in LightRanges)
        {
            if (codePoint >= start && codePoint <= end)
            {
                return LightWeight;
            }
        }

        return DefaultWeight;
    }
}
