using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// Test collection for the classes that relocate the storage root through the process-wide
/// <c>DAYNOTE_DATA_DIR</c> environment variable. Grouping them disables parallel execution across the
/// classes, so one test's temporary <c>DAYNOTE_DATA_DIR</c> can never leak into another's root resolution.
/// </summary>
[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class AppPathsEnvironment
{
    public const string CollectionName = "AppPaths environment (DAYNOTE_DATA_DIR)";
}
