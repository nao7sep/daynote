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
            RecordFormat.DetailsText("""{"path":"/ノート.daynote"}""", null));
        Assert.Equal("not json {", RecordFormat.DetailsText("not json {", null));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"\"")]
    [InlineData("\" \\n \"")]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Fields_with_nothing_in_them_have_no_details(string fields)
    {
        Assert.Null(RecordFormat.DetailsText(fields, null));
    }

    [Fact]
    public void The_note_id_the_note_field_shows_is_left_out_of_the_details()
    {
        Assert.Null(RecordFormat.DetailsText("""{"noteId":"note_7"}""", "note_7"));
        Assert.Equal("{\n  \"path\": \"a\"\n}".Replace("\n", Environment.NewLine),
            RecordFormat.DetailsText("""{"noteId":"note_7","path":"a"}""", "note_7"));
    }

    [Fact]
    public void A_note_id_no_field_shows_stays_in_the_details()
    {
        Assert.Contains("note_7", RecordFormat.DetailsText("""{"noteId":"note_7"}""", null));
        Assert.Contains("note_7", RecordFormat.DetailsText("""{"noteId":"note_7"}""", "note_8"));
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
