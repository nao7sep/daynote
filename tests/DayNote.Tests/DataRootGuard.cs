using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DayNote.Core.Backup;
using DayNote.Core.Storage;
using Xunit.v3;

[assembly: DayNote.Tests.DataRootGuard]

namespace DayNote.Tests;

/// <summary>
/// Keeps every test's storage root in a throwaway folder, never the developer's own <c>~/.daynote</c>.
///
/// The test process starts with <c>DAYNOTE_DATA_DIR</c> pointing at a disposable root of its own,
/// before any test code runs, so a test that relocates nothing, and a late write after a test has put
/// the variable back, both land there. Before and after every test the root the app would resolve must
/// lie in the temporary folder; a test that leaves it anywhere else fails.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class DataRootGuard : BeforeAfterTestAttribute
{
    /// <summary>The process-wide disposable root every test starts from.</summary>
    internal static string SessionRoot { get; } =
        Path.Combine(Path.GetTempPath(), "daynote-tests-" + Guid.NewGuid().ToString("N"));

#pragma warning disable CA2255 // The test assembly is the process; its environment must be set before any test runs.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Install()
    {
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, SessionRoot);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            BackupStore.Close();
            try
            {
                Directory.Delete(SessionRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: a leftover temp directory is harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // Same: a test left something it could not delete; the OS clears the temp folder.
            }
        };
    }

    public override void Before(MethodInfo methodUnderTest, IXunitTest test) => Check(new AppPaths().Root);

    public override void After(MethodInfo methodUnderTest, IXunitTest test) => Check(new AppPaths().Root);

    /// <summary>Throws unless <paramref name="root"/> lies inside the temporary folder.</summary>
    internal static void Check(string root)
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        var full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(temp) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A test resolved the storage root '{full}', outside the temporary folder. Every test must set "
                + $"{AppPaths.HomeEnvironmentVariable} to a throwaway directory, or keep the one the run starts with.");
        }
    }
}
