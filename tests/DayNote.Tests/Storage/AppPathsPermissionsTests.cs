using System;
using System.IO;
using DayNote.Core.Identity;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// The owner-only (<c>0700</c>) storage-root permission rule (storage-path conventions, "The resolver
/// creates the root"): a fresh root is created owner-only, an existing root that is broader gets tightened
/// on the next <see cref="AppPaths.EnsureCreated"/> call, and only the root itself is ever touched — never
/// its contents. Windows has its own permission model and skips the step entirely, so these checks only
/// make sense on POSIX; each test is skipped when running on Windows.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class AppPathsPermissionsTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _home;
    private readonly string? _previousHome;

    public AppPathsPermissionsTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "daynote-permissions-tests-" + IdGenerator.New());
        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _home);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        AppPaths.ConfigureWarn(null!);
        if (Directory.Exists(_home))
        {
            // The root was made owner-only by the test; widen it back before recursive delete so
            // cleanup itself does not fail with access-denied.
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(_home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                catch
                {
                    // best-effort cleanup only
                }
            }

            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public void Fresh_root_is_created_owner_only()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "0700 tightening is POSIX-only; Windows uses its own permission model.");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var paths = new AppPaths();
        paths.EnsureCreated();

        var mode = File.GetUnixFileMode(paths.Root) & (UnixFileMode)0x1FF; // mask to the low 9 permission bits
        Assert.Equal(OwnerOnly, mode);
    }

    [Fact]
    public void Existing_broader_root_is_tightened_on_the_next_call()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "0700 tightening is POSIX-only; Windows uses its own permission model.");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_home);
        File.SetUnixFileMode(_home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); // 0755

        var paths = new AppPaths();
        paths.EnsureCreated();

        var mode = File.GetUnixFileMode(paths.Root) & (UnixFileMode)0x1FF;
        Assert.Equal(OwnerOnly, mode);
    }

    [Fact]
    public void Only_the_root_itself_is_touched_not_its_contents()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "0700 tightening is POSIX-only; Windows uses its own permission model.");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var paths = new AppPaths();
        paths.EnsureCreated();

        var childFile = Path.Combine(paths.Root, "config.json");
        File.WriteAllText(childFile, "{}");
        File.SetUnixFileMode(childFile, UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.OtherRead); // 0644, deliberately broader than 0700

        paths.EnsureCreated();

        // The root stays owner-only, but a broader child file is left exactly as it was: EnsureCreated
        // never walks into the root's contents.
        var rootMode = File.GetUnixFileMode(paths.Root) & (UnixFileMode)0x1FF;
        Assert.Equal(OwnerOnly, rootMode);
        var childMode = File.GetUnixFileMode(childFile) & (UnixFileMode)0x1FF;
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            childMode);
    }

    [Fact]
    public void Warn_sink_can_be_configured_without_affecting_normal_tightening()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "0700 tightening is POSIX-only; Windows uses its own permission model.");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // A real SetUnixFileMode failure is not reliably injectable here (the test process owns the
        // directory it just created, so it can always chmod it) without a fragile, platform-specific
        // trick such as dropping privileges. This test instead pins the two guarantees that ARE
        // observable from outside AppPaths: (1) installing a warn sink never changes the successful
        // tightening outcome, and (2) the sink is only invoked on an actual failure, never speculatively.
        // The catch-and-report itself (AppPaths.EnsureOwnerOnly never lets SetUnixFileMode's exception
        // escape EnsureCreated, reporting through this same sink instead) is exercised by code reading.
        Directory.CreateDirectory(_home);
        File.SetUnixFileMode(_home, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute); // 0750, broader than 0700

        string? warnedMessage = null;
        Exception? warnedError = null;
        AppPaths.ConfigureWarn((message, error) =>
        {
            warnedMessage = message;
            warnedError = error;
        });

        var paths = new AppPaths();
        var exception = Record.Exception(() => paths.EnsureCreated());

        Assert.Null(exception);
        Assert.Null(warnedMessage);
        Assert.Null(warnedError);
        var mode = File.GetUnixFileMode(paths.Root) & (UnixFileMode)0x1FF;
        Assert.Equal(OwnerOnly, mode);
    }
}
