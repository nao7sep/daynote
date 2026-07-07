using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using DayNote;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(DayNote.Tests.TestAppBuilder))]

// Avalonia headless drives every [AvaloniaFact] through one shared application and dispatcher.
// Serialize the assembly so separate test classes cannot claim that dispatcher from different threads.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DayNote.Tests;

/// <summary>
/// Headless Avalonia entry point for the [AvaloniaFact] view-model tests. It reuses the real
/// <see cref="App"/> so the theme resources load, but the headless lifetime is not a classic desktop
/// one, so the app's own startup (which would create the main window and touch the real storage root)
/// is never run.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
