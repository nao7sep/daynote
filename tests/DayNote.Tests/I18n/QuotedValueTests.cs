using System.Linq;
using DayNote.I18n;
using Xunit;

namespace DayNote.Tests.I18n;

/// <summary>
/// A user's value inside a message is shortened as identifying text: a name keeps its beginning and a
/// path keeps its end, each past one limit and with one ellipsis (interface-styling-conventions, Labels).
/// </summary>
public sealed class QuotedValueTests
{
    [Fact]
    public void A_short_value_is_quoted_whole()
    {
        Assert.Equal("Journal", QuotedValue.Name("Journal"));
        Assert.Equal("/Users/me/journal.daynote", QuotedValue.Path("/Users/me/journal.daynote"));
    }

    [Fact]
    public void A_long_name_keeps_its_beginning()
    {
        var name = "Beginning " + new string('x', 300) + ".pdf";

        var quoted = QuotedValue.Name(name);

        Assert.StartsWith("Beginning ", quoted, System.StringComparison.Ordinal);
        Assert.EndsWith("…", quoted, System.StringComparison.Ordinal);
        Assert.True(quoted.Length <= QuotedValue.Limit + 2);
    }

    [Fact]
    public void A_long_path_keeps_its_root_and_its_final_segment()
    {
        var path = "/Users/me/" + string.Join("/", Enumerable.Repeat("deeply-nested-folder", 12)) + "/config.json";

        var quoted = QuotedValue.Path(path);

        Assert.StartsWith("/Users/me/", quoted, System.StringComparison.Ordinal);
        Assert.EndsWith("/config.json", quoted, System.StringComparison.Ordinal);
        Assert.Single(quoted, character => character == '…');
        Assert.Equal(QuotedValue.Limit + 1, quoted.Length);
    }
}
