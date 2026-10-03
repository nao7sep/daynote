using System;
using DayNote.I18n;
using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class RecordFormatTests
{
    [Fact]
    public void Fields_are_indented_for_reading_and_text_that_is_not_json_is_kept()
    {
        Assert.Equal("{\n  \"path\": \"/ノート.daynote\"\n}".Replace("\n", Environment.NewLine),
            RecordFormat.PrettyJson("""{"path":"/ノート.daynote"}"""));
        Assert.Equal("not json {", RecordFormat.PrettyJson("not json {"));
    }

    [Fact]
    public void A_stored_level_reads_in_the_interface_language_and_an_unknown_one_as_stored()
    {
        Assert.Equal(Localizer.T("records.levelWarn"), RecordFormat.LevelText("warn"));
        Assert.Equal("trace", RecordFormat.LevelText("trace"));
    }

    [Fact]
    public void A_time_that_is_not_a_timestamp_shows_as_stored()
    {
        Assert.Equal("yesterday", RecordFormat.TimeText("yesterday", TimeZoneInfo.Utc));
    }
}
