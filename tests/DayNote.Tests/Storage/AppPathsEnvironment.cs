using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// Test collection for the classes that relocate the storage root through the process-wide
/// <c>DAYNOTE_HOME</c> environment variable. Grouping them disables parallel execution across the
/// classes, so one test's temporary <c>DAYNOTE_HOME</c> can never leak into another's root resolution.
/// </summary>
[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class AppPathsEnvironment
{
    public const string CollectionName = "AppPaths environment (DAYNOTE_HOME)";
}
