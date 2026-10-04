using System;
using System.IO;
using System.Reflection;
using DayNote.Core.Storage;
using DayNote.Tests.Storage;
using Xunit;

namespace DayNote.Tests;

/// <summary>
/// The guard that keeps tests out of the developer's own data folder: it is installed on the whole
/// assembly, the run starts on a throwaway root, and a root outside the temporary folder fails it.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class DataRootGuardTests
{
    [Fact]
    public void Every_test_in_the_assembly_is_guarded() =>
        Assert.NotNull(typeof(DataRootGuard).Assembly.GetCustomAttribute<DataRootGuard>());

    [Fact]
    public void A_test_that_relocates_nothing_resolves_the_run_s_throwaway_root()
    {
        Assert.Equal(DataRootGuard.SessionRoot, new AppPaths().Root);
        DataRootGuard.Check(DataRootGuard.SessionRoot);
    }

    [Fact]
    public void The_real_data_folder_fails_the_guard()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var error = Assert.Throws<InvalidOperationException>(() => DataRootGuard.Check(AppPaths.ResolveRoot(null, home)));

        Assert.Contains(Path.Combine(home, ".daynote"), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_throwaway_root_in_the_temporary_folder_passes() =>
        DataRootGuard.Check(Path.Combine(Path.GetTempPath(), "daynote-guard-tests", ".daynote"));
}
