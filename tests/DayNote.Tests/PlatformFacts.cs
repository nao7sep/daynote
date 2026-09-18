using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace DayNote.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that runs only on macOS. On other platforms the
/// test is reported as skipped rather than failing, so the suite stays green on
/// Windows/CI while still covering the mac-specific path on a real mac.
/// </summary>
public sealed class MacOnlyFactAttribute : FactAttribute
{
    // Forward the caller's source location to the base so xunit can report it (xUnit3003).
    public MacOnlyFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Skip = "Runs on macOS only.";
    }
}
