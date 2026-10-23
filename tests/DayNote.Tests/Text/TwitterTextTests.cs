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

    [Theory]
    [InlineData("Visit example.com.", 30)]
    [InlineData("(https://example.com/path).", 26)]
    [InlineData("example.com", 23)]
    [InlineData("example.com/path?x=1", 23)]
    [InlineData("mail@example.com", 16)]
    [InlineData("https://t.co/a/ignored", 31)]
    public void Url_extraction_preserves_sentence_punctuation_and_protocol_boundaries(string text, int expected)
    {
        Assert.Equal(expected, TwitterText.WeightedLength(text));
    }

    [Theory]
    [InlineData("example.com.\u3042.com/path", 29)]
    [InlineData("example.com.\u3042.com/path example.org", 53)]
    [InlineData("\u3042example.com.\u3042.com/path", 31)]
    public void Mixed_script_bare_domains_follow_the_upstream_extracted_url_text_length(string text, int expected)
    {
        // twitter-text 3.1.0 parseTweet counts the reconstructed URL's text length even when its
        // entity's end index includes more of the original domain; the following URL still counts.
        Assert.Equal(expected, TwitterText.WeightedLength(text));
    }

    [Theory]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466")]
    [InlineData("\U0001F44D\U0001F3FD")]
    [InlineData("1\uFE0F\u20E3")]
    [InlineData("\U0001F1EF\U0001F1F5")]
    public void Emoji_sequences_weigh_two_as_a_whole(string text)
    {
        Assert.Equal(2, TwitterText.WeightedLength(text));
    }

    [Theory]
    [InlineData("https://")]
    [InlineData("")]
    public void A_domain_label_longer_than_sixty_three_ascii_characters_is_plain_text(string protocol)
    {
        var text = protocol + new string('a', 64) + ".com";

        Assert.Equal(text.Length, TwitterText.WeightedLength(text));
    }

    [Fact]
    public void An_overlong_punycode_encoded_label_is_plain_text()
    {
        var text = "https://" + new string('あ', 60) + ".com";

        Assert.Equal(8 + 60 * 2 + 4, TwitterText.WeightedLength(text));
    }

    [Fact]
    public void Nfc_normalization_counts_a_decomposed_accent_as_one()
    {
        Assert.Equal(1, TwitterText.WeightedLength("e\u0301"));
    }

    [Theory]
    [InlineData("", 0xD800, "", 2)]
    [InlineData("a", 0xDC00, "", 3)]
    [InlineData("", 0xD800, "https://example.com", 25)]
    public void Malformed_utf16_remains_countable_without_normalization_failure(string prefix, int surrogate, string suffix, int expected)
    {
        // Construct the unpaired surrogate at runtime: attribute metadata and test serialization
        // cannot preserve malformed Unicode strings reliably.
        var text = prefix + (char)surrogate + suffix;

        Assert.Equal(expected, TwitterText.WeightedLength(text));
    }
}
