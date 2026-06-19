using System.IO;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// Path keys back per-notebook locks, and are compared case-insensitively for Windows
/// compatibility, so two spellings of the same path must produce one key.
/// </summary>
public sealed class PathKeyTests
{
    [Fact]
    public void Normalize_returns_a_full_lowercased_path()
    {
        var path = Path.Combine(Path.GetTempPath(), "MixedCase", "File.DAYNOTE");

        Assert.Equal(Path.GetFullPath(path).ToLowerInvariant(), PathKey.Normalize(path));
    }

    [Fact]
    public void Paths_differing_only_in_case_are_equal()
    {
        var upper = Path.Combine(Path.GetTempPath(), "Notebooks", "Journal.daynote");
        var lower = Path.Combine(Path.GetTempPath(), "notebooks", "journal.DayNote");

        Assert.True(PathKey.Equal(upper, lower));
    }

    [Fact]
    public void Different_paths_are_not_equal()
    {
        var a = Path.Combine(Path.GetTempPath(), "a.daynote");
        var b = Path.Combine(Path.GetTempPath(), "b.daynote");

        Assert.False(PathKey.Equal(a, b));
    }
}
