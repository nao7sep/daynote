using DayNote.ViewModels;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class MainWindowEmptyStatesTests
{
    [Theory]
    [InlineData(0, 0, "", "No binders yet. Create or open one.")]
    [InlineData(2, 0, "missing", "No binders match this filter.")]
    [InlineData(2, 1, "missing", "")]
    public void Binders_describe_ordinary_filter_and_populated_states(
        int totalCount,
        int visibleCount,
        string filter,
        string expected)
    {
        Assert.Equal(expected, MainWindowEmptyStates.Binders(totalCount, visibleCount, filter));
    }

    [Theory]
    [InlineData(false, 0, 0, "", "Open or create a binder to add notes.")]
    [InlineData(true, 0, 0, "", "No notes yet. Create one to get started.")]
    [InlineData(true, 2, 0, "missing", "No notes match this filter.")]
    [InlineData(true, 2, 1, "missing", "")]
    public void Notes_describe_prerequisite_ordinary_filter_and_populated_states(
        bool hasBinder,
        int totalCount,
        int visibleCount,
        string filter,
        string expected)
    {
        Assert.Equal(expected, MainWindowEmptyStates.Notes(hasBinder, totalCount, visibleCount, filter));
    }

    [Theory]
    [InlineData(false, 0, "Select or create a note to add attachments.")]
    [InlineData(true, 0, "No attachments yet.")]
    [InlineData(true, 1, "")]
    public void Attachments_describe_prerequisite_ordinary_and_populated_states(
        bool hasSelectedNote,
        int visibleCount,
        string expected)
    {
        Assert.Equal(expected, MainWindowEmptyStates.Attachments(hasSelectedNote, visibleCount));
    }
}
