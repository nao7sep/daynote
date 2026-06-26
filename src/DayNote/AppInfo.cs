using System.Reflection;

namespace DayNote;

/// <summary>
/// The application's identity — name and version — resolved once from the assembly. The single
/// source of truth for both the startup log line and the About dialog, so they can never report
/// different versions.
/// </summary>
internal static class AppInfo
{
    public const string Name = "DayNote";

    /// <summary>The three-part assembly version (e.g. <c>0.1.0</c>), or <c>"unknown"</c> if unavailable.</summary>
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
}
