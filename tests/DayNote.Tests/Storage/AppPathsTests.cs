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

    [Fact]
    public void Bare_Tilde_Override_Expands_To_Home()
    {
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, "~");

        var paths = new AppPaths();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.GetFullPath(home), paths.Root);
    }

    [Fact]
    public void Leading_Tilde_Override_Expands_Against_Home()
    {
        var leaf = "daynote-tilde-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, "~/" + leaf);

        var paths = new AppPaths();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.GetFullPath(Path.Combine(home, leaf)), paths.Root);
    }

    [Fact]
    public void Override_Expands_Environment_References()
    {
        // The resolver expands the %VAR% form (here) as well as the POSIX $VAR / ${VAR} forms
        // (covered by the test below). Use a uniquely named variable so the test is independent of
        // the ambient environment, and restore it afterwards.
        var variableName = "DAYNOTE_EXPAND_TEST_" + Guid.NewGuid().ToString("N");
        var expansion = Path.Combine(Path.GetTempPath(), "daynote-expand-" + Guid.NewGuid().ToString("N"));
        var previousValue = Environment.GetEnvironmentVariable(variableName);
        try
        {
            Environment.SetEnvironmentVariable(variableName, expansion);
            Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, "%" + variableName + "%");

            var paths = new AppPaths();

            Assert.Equal(Path.GetFullPath(expansion), paths.Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
        }
    }

    [Fact]
    public void Override_Expands_Dollar_Environment_References()
    {
        var variableName = "DAYNOTE_EXPAND_TEST_" + Guid.NewGuid().ToString("N");
        var expansion = Path.Combine(Path.GetTempPath(), "daynote-dollar-" + Guid.NewGuid().ToString("N"));
        var previousValue = Environment.GetEnvironmentVariable(variableName);
        try
        {
            Environment.SetEnvironmentVariable(variableName, expansion);

            Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, "$" + variableName);
            Assert.Equal(Path.GetFullPath(expansion), new AppPaths().Root);

            Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, "${" + variableName + "}");
            Assert.Equal(Path.GetFullPath(expansion), new AppPaths().Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
        }
    }

    [Fact]
    public void Override_That_Expands_To_Empty_Is_Rejected()
    {
        // A reference to a variable that is definitely unset expands to empty; that is a
        // misconfiguration, reported rather than silently collapsing onto the home directory.
        var unsetVariable = "DAYNOTE_UNSET_PROBE_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(unsetVariable, null);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, "$" + unsetVariable);

        Assert.Throws<InvalidOperationException>(() => new AppPaths());
    }
}
