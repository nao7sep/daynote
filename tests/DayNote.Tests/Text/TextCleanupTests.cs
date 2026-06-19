using DayNote.Core.Text;
using Xunit;

namespace DayNote.Tests.Text;

/// <summary>
/// DayNote's local copy of the text-cleanup-conventions' truncation pattern, used to derive a list
/// label from a note body. The contracts that matter: single-line output, a minimum (not exact)
/// length, honest <c>Truncated</c> reporting that ignores an all-whitespace tail, and grapheme safety
/// so emoji and combining sequences are never split.
/// </summary>
public sealed class TextCleanupTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Truncate_returns_empty_and_untruncated_for_a_nonpositive_budget(int n)
    {
        var result = TextCleanup.Truncate("anything", n);
        Assert.Equal(string.Empty, result.Text);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Truncate_collapses_newlines_and_whitespace_into_single_spaces()
    {
        var result = TextCleanup.Truncate("line one\n\n  line  two ", 100);
        Assert.Equal("line one line two", result.Text);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Truncate_drops_leading_and_trailing_whitespace()
    {
        Assert.Equal("hello", TextCleanup.Truncate("   hello   ", 100).Text);
    }

    [Fact]
    public void Truncate_stops_at_the_minimum_length_and_reports_a_cut()
    {
        var result = TextCleanup.Truncate("abcdef", 3);
        Assert.Equal("abc", result.Text);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Truncate_does_not_report_a_cut_for_an_all_whitespace_tail()
    {
        var result = TextCleanup.Truncate("abc   \n   ", 3);
        Assert.Equal("abc", result.Text);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Truncate_never_splits_a_combining_sequence()
    {
        // Base 'a' + combining acute (U+0301) is one grapheme of two code points; budget 1 keeps it whole.
        var result = TextCleanup.Truncate("ábc", 1);
        Assert.Equal("á", result.Text);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Truncate_never_splits_an_emoji()
    {
        var result = TextCleanup.Truncate("\U0001F44Dok", 1);
        Assert.Equal("\U0001F44D", result.Text);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void SingleLine_flattens_line_breaks_and_trims_but_keeps_interior_spacing()
    {
        Assert.Equal("a b", TextCleanup.SingleLine("  a\n\n  b  "));
        Assert.Equal("a  b", TextCleanup.SingleLine("a  b")); // pure horizontal spacing preserved
    }

    [Fact]
    public void SingleLine_minify_collapses_every_whitespace_run_including_full_width_space()
    {
        Assert.Equal("a b c", TextCleanup.SingleLine("a　　b  c", minify: true));
    }

    [Fact]
    public void SingleLine_with_flatten_off_only_trims()
    {
        Assert.Equal("a\nb", TextCleanup.SingleLine("  a\nb  ", flattenLineBreaks: false));
    }

    [Fact]
    public void Multiline_trims_line_ends_and_normalizes_newlines()
    {
        Assert.Equal("a\nb\nc", TextCleanup.Multiline("a   \r\nb\t\nc"));
    }

    [Fact]
    public void Multiline_drops_edge_blanks_and_keeps_interior_blanks_by_default()
    {
        Assert.Equal("x\n\ny", TextCleanup.Multiline("\n\nx\n\ny\n\n"));
        Assert.Equal("a\n\n\nb", TextCleanup.Multiline("a\n\n\nb")); // interior run preserved
    }

    [Fact]
    public void Multiline_collapses_interior_blank_runs_only_when_asked()
    {
        Assert.Equal("a\n\nb", TextCleanup.Multiline("a\n\n\nb", collapseBlankLines: true));
    }
}
