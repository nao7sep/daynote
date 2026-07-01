using DayNote.Core.Backup;
using Xunit;

namespace DayNote.Tests.Backup;

/// <summary>The mirror-layout mapping: home files at their relative path, binders and assets under
/// <c>binders/&lt;id&gt;/</c>, all with forward slashes.</summary>
public sealed class BackupArchivePathsTests
{
    [Fact]
    public void Home_File_Keeps_Its_Relative_Path()
    {
        Assert.Equal("config.json", BackupArchivePaths.ForHomeFile("config.json"));
    }

    [Fact]
    public void Backslashes_Become_Forward_Slashes()
    {
        Assert.Equal("a/b/c.txt", BackupArchivePaths.ForHomeFile("a\\b\\c.txt"));
    }

    [Fact]
    public void Binder_File_Is_Under_Its_Id()
    {
        Assert.Equal(
            "binders/ID1/My Journal.daynote",
            BackupArchivePaths.ForBinderFile("ID1", "My Journal.daynote"));
    }

    [Fact]
    public void Binder_Asset_Is_Under_Assets_Namespaced_By_Id()
    {
        Assert.Equal(
            "binders/ID1/assets/note9/pic.jpg",
            BackupArchivePaths.ForBinderAsset("ID1", "note9/pic.jpg"));

        Assert.Equal(
            "binders/ID1/assets/note9/pic.jpg",
            BackupArchivePaths.ForBinderAsset("ID1", "note9\\pic.jpg"));
    }

    [Fact]
    public void Normalize_Strips_A_Leading_Slash()
    {
        Assert.Equal("config.json", BackupArchivePaths.Normalize("/config.json"));
    }
}
