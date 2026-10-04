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
            RecordFormat.SplitFields("""{"path":"/ノート.daynote"}""", null).Details);
        Assert.Equal("not json {", RecordFormat.SplitFields("not json {", null).Details);
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
        Assert.Null(RecordFormat.SplitFields(fields, null).Details);
    }

    [Fact]
    public void The_note_id_the_note_field_shows_is_left_out_of_the_details()
    {
        Assert.Null(RecordFormat.SplitFields("""{"noteId":"note_7"}""", "note_7").Details);
        Assert.Equal("{\n  \"path\": \"a\"\n}".Replace("\n", Environment.NewLine),
            RecordFormat.SplitFields("""{"noteId":"note_7","path":"a"}""", "note_7").Details);
    }

    [Fact]
    public void A_note_id_no_field_shows_stays_in_the_details()
    {
        Assert.Contains("note_7", RecordFormat.SplitFields("""{"noteId":"note_7"}""", null).Details);
        Assert.Contains("note_7", RecordFormat.SplitFields("""{"noteId":"note_7"}""", "note_8").Details);
    }

    [Fact]
    public void An_attached_error_is_its_own_block_and_left_out_of_the_details()
    {
        var (details, error) = RecordFormat.SplitFields(
            """{"path":"a","error":{"type":"System.IO.IOException","message":"denied"}}""", null);

        Assert.Equal("{\n  \"path\": \"a\"\n}".Replace("\n", Environment.NewLine), details);
        Assert.Equal("{\n  \"type\": \"System.IO.IOException\",\n  \"message\": \"denied\"\n}".Replace("\n", Environment.NewLine), error);
    }

    [Fact]
    public void An_error_with_nothing_beside_it_leaves_no_details()
    {
        var (details, error) = RecordFormat.SplitFields("""{"noteId":"note_7","error":{"message":"denied"}}""", "note_7");

        Assert.Null(details);
        Assert.Contains("denied", error);
    }

    [Theory]
    [InlineData("""{"path":"a"}""")]
    [InlineData("""{"path":"a","error":null}""")]
    [InlineData("""{"path":"a","error":{}}""")]
    [InlineData("not json {")]
    public void A_record_without_an_error_has_no_error_block(string fields)
    {
        Assert.Null(RecordFormat.SplitFields(fields, null).Error);
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
