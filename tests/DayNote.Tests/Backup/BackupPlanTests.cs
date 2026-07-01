using System;
using System.Collections.Generic;
using DayNote.Core.Backup;
using DayNote.Core.Time;
using Xunit;

namespace DayNote.Tests.Backup;

/// <summary>
/// The pure change decision: a file is captured when its size or modification time differs from the latest
/// index entry for its archive path, with a two-second mtime tolerance and no content hashing.
/// </summary>
public sealed class BackupPlanTests
{
    private static readonly DateTimeOffset Base =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static BackupCandidate Candidate(string path, long size, DateTimeOffset mtime) =>
        new("/abs/" + path, path, size, mtime);

    private static BackupIndexEntry Entry(string archivedAt, string path, long size, DateTimeOffset mtime) =>
        new()
        {
            ArchivedAt = archivedAt,
            ArchivePath = path,
            SizeBytes = size,
            LastWriteUtc = DayNoteTime.ToIsoSeconds(mtime),
        };

    [Fact]
    public void A_File_With_No_Prior_Entry_Is_New()
    {
        var changed = BackupPlan.SelectChanged(
            new[] { Candidate("config.json", 10, Base) },
            new BackupIndex());

        Assert.Single(changed);
        Assert.Equal("config.json", changed[0].ArchivePath);
    }

    [Fact]
    public void Same_Size_And_Same_Mtime_Is_Unchanged()
    {
        var index = new BackupIndex { Entries = { Entry("20260701-120000-utc", "config.json", 10, Base) } };

        var changed = BackupPlan.SelectChanged(new[] { Candidate("config.json", 10, Base) }, index);

        Assert.Empty(changed);
    }

    [Fact]
    public void A_Different_Size_Is_Changed()
    {
        var index = new BackupIndex { Entries = { Entry("20260701-120000-utc", "config.json", 10, Base) } };

        var changed = BackupPlan.SelectChanged(new[] { Candidate("config.json", 11, Base) }, index);

        Assert.Single(changed);
    }

    [Fact]
    public void Mtime_Within_Two_Seconds_Is_Unchanged()
    {
        var index = new BackupIndex { Entries = { Entry("20260701-120000-utc", "config.json", 10, Base) } };

        var changed = BackupPlan.SelectChanged(
            new[] { Candidate("config.json", 10, Base.AddSeconds(2)) }, index);

        Assert.Empty(changed);
    }

    [Fact]
    public void Mtime_Beyond_Two_Seconds_Is_Changed()
    {
        var index = new BackupIndex { Entries = { Entry("20260701-120000-utc", "config.json", 10, Base) } };

        var changed = BackupPlan.SelectChanged(
            new[] { Candidate("config.json", 10, Base.AddSeconds(3)) }, index);

        Assert.Single(changed);
    }

    [Fact]
    public void Comparison_Uses_The_Latest_Entry_For_The_Path()
    {
        // Two versions recorded; the newer one has size 20. A candidate matching the OLDER size (10) is
        // still changed, because only the latest state is trusted.
        var index = new BackupIndex
        {
            Entries =
            {
                Entry("20260701-120000-utc", "config.json", 10, Base),
                Entry("20260701-130000-utc", "config.json", 20, Base),
            },
        };

        Assert.Empty(BackupPlan.SelectChanged(new[] { Candidate("config.json", 20, Base) }, index));
        Assert.Single(BackupPlan.SelectChanged(new[] { Candidate("config.json", 10, Base) }, index));
    }

    [Fact]
    public void An_Unparseable_Stored_Timestamp_Forces_Recapture()
    {
        var entry = Entry("20260701-120000-utc", "config.json", 10, Base);
        entry.LastWriteUtc = "not-a-timestamp";
        var index = new BackupIndex { Entries = { entry } };

        var changed = BackupPlan.SelectChanged(new[] { Candidate("config.json", 10, Base) }, index);

        Assert.Single(changed);
    }
}
