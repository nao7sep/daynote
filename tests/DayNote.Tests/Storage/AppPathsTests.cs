using System;
using System.IO;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// Storage-root resolution: <c>DAYNOTE_DATA_DIR</c> relocates the whole tree when set, the default
/// <c>~/.daynote</c> is used when it is not, and a relative override resolves against the home
/// directory (never the working directory) so no path can depend on how the app was launched. Every
/// case resolves against a throwaway home, never the real one.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class AppPathsTests : IDisposable
{
    private readonly string? _previousHome;
    private readonly string _home = Path.Combine(Path.GetTempPath(), "daynote-home-tests-" + IdGenerator.New());

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
        Assert.Equal(Path.Combine(_home, ".daynote"), AppPaths.ResolveRoot(null, _home));
    }

    [Fact]
    public void Override_Relocates_The_Whole_Root()
    {
        var target = Path.Combine(Path.GetTempPath(), "daynote-home-tests-" + IdGenerator.New());
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, target);

        var paths = new AppPaths();

        Assert.Equal(Path.GetFullPath(target), Path.GetFullPath(paths.Root));
        // Every subpath is derived from the relocated root.
        Assert.Equal(Path.Combine(paths.Root, "config.json"), paths.ConfigFile);
        Assert.Equal(Path.Combine(paths.Root, "records.sqlite3"), paths.RecordsFile);
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.LogsDirectory);
    }

    [Fact]
    public void Empty_Override_Falls_Back_To_The_Default()
    {
        Assert.Equal(Path.Combine(_home, ".daynote"), AppPaths.ResolveRoot("   ", _home));
    }

    [Fact]
    public void Relative_Override_Resolves_Against_Home_Not_Working_Directory()
    {
        var relative = "daynote-relative-" + IdGenerator.New();

        var root = AppPaths.ResolveRoot(relative, _home);

        Assert.Equal(Path.GetFullPath(Path.Combine(_home, relative)), root);
        Assert.NotEqual(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relative)), root);
    }

    [Fact]
    public void Bare_Tilde_Override_Expands_To_Home()
    {
        Assert.Equal(Path.GetFullPath(_home), AppPaths.ResolveRoot("~", _home));
    }

    [Fact]
    public void Leading_Tilde_Override_Expands_Against_Home()
    {
        var leaf = "daynote-tilde-" + IdGenerator.New();

        Assert.Equal(Path.GetFullPath(Path.Combine(_home, leaf)), AppPaths.ResolveRoot("~/" + leaf, _home));
    }

    [Fact]
    public void Override_Expands_Environment_References()
    {
        // The resolver expands the %VAR% form (here) as well as the POSIX $VAR / ${VAR} forms
        // (covered by the test below). Use a uniquely named variable so the test is independent of
        // the ambient environment, and restore it afterwards.
        // The generated suffix must itself be a valid $VAR/%VAR% identifier (letters, digits,
        // underscore only), so a nanoid's occasional hyphen is folded to an underscore.
        var variableName = "DAYNOTE_EXPAND_TEST_" + IdGenerator.New().Replace('-', '_');
        var expansion = Path.Combine(Path.GetTempPath(), "daynote-expand-" + IdGenerator.New());
        var previousValue = Environment.GetEnvironmentVariable(variableName);
        try
        {
            Environment.SetEnvironmentVariable(variableName, expansion);

            Assert.Equal(Path.GetFullPath(expansion), AppPaths.ResolveRoot("%" + variableName + "%", _home));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
        }
    }

    [Fact]
    public void Override_Expands_Dollar_Environment_References()
    {
        // Same identifier-safety note as above: fold any hyphen so the name stays a valid $VAR/${VAR}.
        var variableName = "DAYNOTE_EXPAND_TEST_" + IdGenerator.New().Replace('-', '_');
        var expansion = Path.Combine(Path.GetTempPath(), "daynote-dollar-" + IdGenerator.New());
        var previousValue = Environment.GetEnvironmentVariable(variableName);
        try
        {
            Environment.SetEnvironmentVariable(variableName, expansion);

            Assert.Equal(Path.GetFullPath(expansion), AppPaths.ResolveRoot("$" + variableName, _home));
            Assert.Equal(Path.GetFullPath(expansion), AppPaths.ResolveRoot("${" + variableName + "}", _home));
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
        // Same identifier-safety note: fold any hyphen so the name stays a valid $VAR reference.
        var unsetVariable = "DAYNOTE_UNSET_PROBE_" + IdGenerator.New().Replace('-', '_');
        Environment.SetEnvironmentVariable(unsetVariable, null);

        Assert.Throws<InvalidOperationException>(() => AppPaths.ResolveRoot("$" + unsetVariable, _home));
    }
}
