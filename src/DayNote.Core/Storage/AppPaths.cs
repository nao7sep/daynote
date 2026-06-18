namespace DayNote.Core.Storage;

/// <summary>
/// Resolves the locations of the application's own files under <c>~/.daynote/</c>: configuration,
/// session state, the backup database, logs, and per-notebook locks.
/// </summary>
/// <remarks>
/// The root is <c>DAYNOTE_HOME</c> when that environment variable is set and non-empty (the value is
/// expanded for a leading <c>~</c> and for environment references, then made absolute against the home
/// directory), otherwise the default <c>~/.daynote/</c>. The working directory is never a base for any
/// path, per the storage-path conventions. <c>DAYNOTE_HOME</c> is the one relocation seam, used the same
/// way by tests and in production.
/// </remarks>
public sealed class AppPaths
{
    /// <summary>Environment variable that relocates the entire storage root.</summary>
    public const string HomeEnvironmentVariable = "DAYNOTE_HOME";

    public AppPaths() => Root = ResolveRoot();

    public string Root { get; }

    public string ConfigFile => Path.Combine(Root, "config.json");
    public string StateFile => Path.Combine(Root, "state.json");
    public string BackupDatabase => Path.Combine(Root, "backups.sqlite");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string LocksDirectory => Path.Combine(Root, "locks");

    /// <summary>Creates the root and logs directories if they do not yet exist.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }

    private static string ResolveRoot()
    {
        var home = HomeDirectory();

        var overrideValue = Environment.GetEnvironmentVariable(HomeEnvironmentVariable)?.Trim();
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return ResolveOverride(overrideValue, home);
        }

        return Path.Combine(home, ".daynote");
    }

    /// <summary>
    /// Expands a leading <c>~</c> and any environment references in the override value, then makes it
    /// absolute against the home directory (never the working directory) so the override can never
    /// reintroduce a cwd dependence.
    /// </summary>
    private static string ResolveOverride(string value, string home)
    {
        if (value == "~")
        {
            value = home;
        }
        else if (value.StartsWith("~/", StringComparison.Ordinal) ||
                 value.StartsWith("~" + Path.DirectorySeparatorChar))
        {
            value = Path.Combine(home, value[2..]);
        }

        value = Environment.ExpandEnvironmentVariables(value);

        // A relative override is resolved against the home directory, not the working directory.
        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(home, value));
    }

    private static string HomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME");
        }

        if (string.IsNullOrEmpty(home))
        {
            // The convention forbids the working directory as any base; with no home directory there
            // is no usable storage root, so fail loudly rather than silently writing under the cwd.
            throw new InvalidOperationException(
                "Cannot resolve a storage root: the user's home directory is unknown. " +
                "Set the home directory or " + HomeEnvironmentVariable + " to an absolute path.");
        }

        return home;
    }
}
