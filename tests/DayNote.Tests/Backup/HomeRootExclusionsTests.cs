using DayNote.Core.Backup;
using Xunit;

namespace DayNote.Tests.Backup;

/// <summary>The home-root exclude list: durable data is kept, throwaway and self-managed paths are dropped.</summary>
public sealed class HomeRootExclusionsTests
{
    [Theory]
    [InlineData("config.json")]
    [InlineData("notes/data.json")]
    [InlineData("logs")]              // a file literally named "logs" is not the logs/ directory
    [InlineData("sub\\file.txt")]     // backslashes normalize, still included
    public void Durable_Files_Are_Included(string relativePath)
    {
        Assert.False(HomeRootExclusions.IsExcluded(relativePath));
    }

    [Theory]
    [InlineData("state.json")]
    [InlineData("logs/20260701.jsonl")]
    [InlineData("backups/index.json")]
    [InlineData("backups/backup-20260701-120000-000-utc.zip")]
    [InlineData("config-V1StGXR8Z5jdHi6BUtaB.tmp")]
    [InlineData("CONFIG-V1STGXR8Z5JDHI6BUTAB.TMP")]
    [InlineData(".DS_Store")]
    [InlineData("notes/.DS_Store")]                              // OS litter at any depth
    [InlineData("Thumbs.db")]
    [InlineData("thumbs.db")]                                    // matched case-insensitively
    [InlineData("desktop.ini")]                                 // Explorer folder-metadata (fleet floor)
    [InlineData("Desktop.ini")]
    public void Throwaway_And_Self_Managed_Paths_Are_Excluded(string relativePath)
    {
        Assert.True(HomeRootExclusions.IsExcluded(relativePath));
    }
}
