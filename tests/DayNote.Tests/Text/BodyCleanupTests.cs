using DayNote.Core.Text;
using Xunit;

namespace DayNote.Tests.Text;

/// <summary>
/// Body normalization runs on every read and write, so its edge cases (line endings, control
/// characters, outer blank lines) directly shape what is stored and what diffs look like.
/// </summary>
public sealed class BodyCleanupTests
{
    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Equal(string.Empty, BodyCleanup.Normalize(string.Empty));
    }

    [Fact]
    public void Crlf_is_collapsed_to_lf()
    {
        Assert.Equal("a\nb\nc", BodyCleanup.Normalize("a\r\nb\r\nc"));
    }

    [Fact]
    public void Lone_cr_is_collapsed_to_lf()
    {
        Assert.Equal("a\nb", BodyCleanup.Normalize("a\rb"));
    }

    [Fact]
    public void Leading_and_trailing_blank_lines_are_stripped_but_interior_blanks_are_kept()
    {
        Assert.Equal("foo\n\nbar", BodyCleanup.Normalize("\n  \nfoo\n\nbar\n\t\n  \n"));
    }

    [Fact]
    public void Whitespace_only_input_becomes_empty()
    {
        Assert.Equal(string.Empty, BodyCleanup.Normalize("  \n\t\n   "));
    }

    [Fact]
    public void Control_characters_other_than_tab_and_newline_are_removed()
    {
        // NUL and BEL (between a and b) and DEL (before d) are stripped; the tab survives.
        Assert.Equal("ab\tcd", BodyCleanup.Normalize("a\u0000\u0007b\tc\u007Fd"));
    }

    [Fact]
    public void Tabs_and_indentation_are_preserved_verbatim()
    {
        Assert.Equal("first\n\tindented\nlast", BodyCleanup.Normalize("first\n\tindented\nlast"));
    }

    [Fact]
    public void Trailing_whitespace_on_content_lines_is_trimmed()
    {
        // Per the text-cleanup-conventions multiline default; leading indentation stays untouched.
        Assert.Equal("a\n\tb\nc", BodyCleanup.Normalize("a   \n\tb\t \nc"));
    }
}
