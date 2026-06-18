using System;
using System.IO;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// Storage-root resolution: <c>DAYNOTE_HOME</c> relocates the whole tree when set, the default
/// <c>~/.daynote</c> is used when it is not, and a relative override resolves against the home
/// directory (never the working directory) so no path can depend on how the app was launched.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class AppPathsTests : IDisposable
{
    private readonly string? _previousHome;

    public AppPathsTests()
    {
        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
    }

    [Fact]
    public void Root_Defaults_To_DotDaynote_When_Override_Unset()
    {
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, null);

        var paths = new AppPaths();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".daynote"), paths.Root);
    }

    [Fact]
    public void Override_Relocates_The_Whole_Root()
    {
        var target = Path.Combine(Path.GetTempPath(), "daynote-home-tests-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, target);

        var paths = new AppPaths();

        Assert.Equal(Path.GetFullPath(target), Path.GetFullPath(paths.Root));
        // Every subpath is derived from the relocated root.
        Assert.Equal(Path.Combine(paths.Root, "config.json"), paths.ConfigFile);
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.LogsDirectory);
    }

    [Fact]
    public void Empty_Override_Falls_Back_To_The_Default()
    {
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, "   ");

        var paths = new AppPaths();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".daynote"), paths.Root);
    }

    [Fact]
    public void Relative_Override_Resolves_Against_Home_Not_Working_Directory()
    {
        var relative = "daynote-relative-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, relative);

        var paths = new AppPaths();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.GetFullPath(Path.Combine(home, relative)), paths.Root);
        Assert.NotEqual(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relative)), paths.Root);
    }
}
