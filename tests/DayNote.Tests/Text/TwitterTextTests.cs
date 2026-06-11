using DayNote.Core.Text;
using Xunit;

namespace DayNote.Tests.Text;

/// <summary>
/// The X/Twitter weighted length drives the drafting budget shown in the status bar: Latin text
/// weighs 1 per character, most other scripts weigh 2, and URLs are counted at a fixed 23.
/// </summary>
public sealed class TwitterTextTests
{
    [Fact]
    public void Empty_text_weighs_zero()
    {
        Assert.Equal(0, TwitterText.WeightedLength(string.Empty));
    }

    [Fact]
    public void Ascii_weighs_one_per_character()
    {
        Assert.Equal(5, TwitterText.WeightedLength("hello"));
    }

    [Theory]
    [InlineData("あ", 2)]
    [InlineData("日本語", 6)]
    [InlineData("😀", 2)]
    public void Non_latin_characters_weigh_two(string text, int expected)
    {
        Assert.Equal(expected, TwitterText.WeightedLength(text));
    }

    [Fact]
    public void A_url_with_a_scheme_is_counted_as_twenty_three_regardless_of_length()
    {
        Assert.Equal(23, TwitterText.WeightedLength("https://example.com/a/very/long/path/that/is/way/over/23"));
    }

    [Fact]
    public void A_www_url_is_counted_as_twenty_three()
    {
        Assert.Equal(23, TwitterText.WeightedLength("www.example.com/some/path"));
    }

    [Fact]
    public void Text_around_a_url_adds_to_the_url_weight()
    {
        // "a " (2) + url (23) + " b" (2) = 27.
        Assert.Equal(27, TwitterText.WeightedLength("a https://example.com b"));
    }

    [Fact]
    public void The_budget_is_two_hundred_eighty()
    {
        Assert.Equal(280, TwitterText.MaxWeightedLength);
    }
}
