namespace DayNote.Core.Text;

/// <summary>
/// Word, character, and X/Twitter weighted-character counts for a body of text, as shown in the
/// status bar.
/// </summary>
public readonly record struct TextCounts(
    int Words,
    int Chars,
    int XWeightedChars,
    int XLimit,
    bool XWithinLimit);
