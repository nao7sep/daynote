using System;
using System.IO;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// Pure comparisons preserve distinct names; native admission resolves existing aliases separately.
/// </summary>
public sealed class PathKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daynote-paths-" + Guid.NewGuid());

    public PathKeyTests() => Directory.CreateDirectory(_directory);
    [Fact]
    public void Normalize_returns_a_full_path_without_changing_case()
    {
        var path = Path.Combine(Path.GetTempPath(), "MixedCase", "File.DAYNOTE");

        Assert.Equal(Path.GetFullPath(path), PathKey.Normalize(path));
    }

    [Fact]
    public void Distinct_resolved_paths_differing_only_in_case_are_not_equal()
    {
        var upper = Path.Combine(Path.GetTempPath(), "Binders", "Journal.daynote");
        var lower = Path.Combine(Path.GetTempPath(), "binders", "journal.DayNote");

        Assert.False(PathKey.Equal(upper, lower));
    }

    [Fact]
    public void An_unavailable_native_name_is_not_reported_as_a_resolved_spelling()
    {
        var missing = Path.Combine(_directory, "missing.daynote");
        Assert.Null(PathKey.ResolveExisting(missing));
        Assert.Equal(missing, PathKey.Resolve(missing));
    }

    [Fact]
    public void Different_paths_are_not_equal()
    {
        var a = Path.Combine(Path.GetTempPath(), "a.daynote");
        var b = Path.Combine(Path.GetTempPath(), "b.daynote");

        Assert.False(PathKey.Equal(a, b));
    }

    [Fact]
    public void Resolution_keeps_the_stored_name_and_survives_atomic_replacement()
    {
        var file = Path.Combine(_directory, "MixedCase.daynote");
        AtomicFile.WriteAllText(file, "first", recordBackup: false);
        var identity = PathKey.Resolve(file);
        Assert.EndsWith("MixedCase.daynote", identity, StringComparison.Ordinal);
        AtomicFile.WriteAllText(file, "second", recordBackup: false);
        Assert.Equal(identity, PathKey.Resolve(file));
    }

    [MacOnlyFact]
    public void A_file_link_keeps_its_own_binder_namespace_across_replacement()
    {
        var target = Path.Combine(_directory, "target.daynote");
        var link = Path.Combine(_directory, "Link.daynote");
        File.WriteAllText(target, "target bytes");
        File.CreateSymbolicLink(link, target);
        var identity = PathKey.Resolve(link);
        Assert.NotEqual(PathKey.Resolve(target), identity);
        var alias = Path.Combine(_directory, "link.DAYNOTE");
        if (File.Exists(alias))
            Assert.Equal(identity, PathKey.Resolve(alias));
        else
            Assert.NotEqual(identity, PathKey.Resolve(alias));
        AtomicFile.WriteAllText(link, "replacement", recordBackup: false);
        Assert.Equal(identity, PathKey.Resolve(link));
        Assert.Equal("target bytes", File.ReadAllText(target));
    }

    [MacOnlyFact]
    public void Mac_case_aliases_resolve_to_the_stored_name_on_the_ordinary_test_volume() => CheckCaseAlias();

    [WindowsOnlyFact]
    public void Windows_case_aliases_resolve_to_the_stored_name_on_the_ordinary_test_volume() => CheckCaseAlias();

    private void CheckCaseAlias()
    {
        var file = Path.Combine(_directory, "MixedCase.daynote");
        File.WriteAllText(file, "binder");
        var alias = Path.Combine(_directory, "mixedcase.DAYNOTE");
        // The runtime may itself use a case-sensitive temp volume. Distinct files are handled by exact
        // names; this check concerns a spelling the environment actually resolves to the existing file.
        if (File.Exists(alias))
            Assert.Equal(PathKey.Resolve(file), PathKey.Resolve(alias));
        else
            Assert.NotEqual(PathKey.Resolve(file), PathKey.Resolve(alias));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(@"C:\folder\file.txt", @"\\?\C:\folder\file.txt")]
    [InlineData(@"\\server\share\file.txt", @"\\?\UNC\server\share\file.txt")]
    [InlineData(@"\\?\C:\folder\file.txt", @"\\?\C:\folder\file.txt")]
    public void Windows_native_calls_preserve_extended_paths(string path, string expected) =>
        Assert.Equal(expected, PathKey.WindowsExtendedPath(path));
}
