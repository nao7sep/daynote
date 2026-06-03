namespace DayNote.Core.Storage;

/// <summary>
/// Resolves the locations of the application's own files under <c>~/.daynote/</c>: configuration,
/// session state, the backup database, logs, and per-notebook locks.
/// </summary>
public sealed class AppPaths
{
    public AppPaths() : this(DefaultRoot())
    {
    }

    public AppPaths(string root) => Root = root;

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

    private static string DefaultRoot() => Path.Combine(HomeDirectory(), ".daynote");

    private static string HomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? Directory.GetCurrentDirectory();
        }

        return home;
    }
}
