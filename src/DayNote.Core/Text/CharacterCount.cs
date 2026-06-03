using System.Text;

namespace DayNote.Core.Text;

/// <summary>
/// Counts words, Unicode characters, and the X/Twitter weighted-character length of a body of
/// text. Ported from quickdeck, where characters were counted by code point and the weighted
/// length came from twitter-text.
/// </summary>
public static class CharacterCount
{
    /// <summary>Computes all counts for <paramref name="text"/>.</summary>
    public static TextCounts Count(string text)
    {
        var weighted = TwitterText.WeightedLength(text);
        return new TextCounts(
            Words: Words(text),
            Chars: Chars(text),
            XWeightedChars: weighted,
            XLimit: TwitterText.MaxWeightedLength,
            XWithinLimit: weighted <= TwitterText.MaxWeightedLength);
    }

    /// <summary>Number of whitespace-separated words, mirroring quickdeck's <c>trim().split(/\s+/)</c>.</summary>
    public static int Words(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return 0;
        }

        return trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>Number of Unicode code points, matching quickdeck's <c>Array.from(text).length</c>.</summary>
    public static int Chars(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
