using DayNote.Core.Text;
using Xunit;

namespace DayNote.Tests.Text;

/// <summary>
/// Word and code-point counts shown in the status bar. Characters are counted by Unicode code point
/// (so an emoji is one character), which is intentionally different from the X weighted length.
/// </summary>
public sealed class CharacterCountTests
{
    [Theory]
    [InlineData("hello world", 2)]
    [InlineData("   ", 0)]
    [InlineData("", 0)]
    [InlineData("one\ntwo\tthree", 3)]
    [InlineData("  spaced   out  ", 2)]
    public void Words_counts_whitespace_separated_tokens(string text, int expected)
    {
        Assert.Equal(expected, CharacterCount.Words(text));
    }

    [Theory]
    [InlineData("hello", 5)]
    [InlineData("", 0)]
    [InlineData("日本語", 3)]
    public void Chars_counts_code_points(string text, int expected)
    {
        Assert.Equal(expected, CharacterCount.Chars(text));
    }

    [Fact]
    public void A_surrogate_pair_counts_as_one_character_but_weighs_two()
    {
        var counts = CharacterCount.Count("😀");

        Assert.Equal(1, counts.Chars);
        Assert.Equal(2, counts.XWeightedChars);
    }

    [Fact]
    public void Count_reports_the_limit_and_within_limit_flag()
    {
        var within = CharacterCount.Count("short");
        Assert.Equal(280, within.XLimit);
        Assert.True(within.XWithinLimit);

        // 141 wide characters weigh 282, just over the 280 budget.
        var over = CharacterCount.Count(new string('あ', 141));
        Assert.False(over.XWithinLimit);
    }
}
