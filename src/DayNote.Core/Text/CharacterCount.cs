using System.Text;

namespace DayNote.Core.Text;

/// <summary>
/// Counts words, Unicode characters, and the X/Twitter weighted-character length of a body of
/// text. The weighted length follows twitter-text's rules, so the X budget the status bar shows
/// matches what X itself will count.
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

    /// <summary>
    /// Number of whitespace-separated words. A run of spaces, tabs or line breaks is one separator, so
    /// a writer's spacing never changes the count; a language written without spaces counts as one word
    /// per run, which is why the character count stands beside it.
    /// </summary>
    public static int Words(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return 0;
        }

        return trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Number of Unicode code points, so an emoji or a character outside the Basic Multilingual Plane
    /// counts as one, as a reader sees it, rather than as the two UTF-16 units it is stored in.
    /// </summary>
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
