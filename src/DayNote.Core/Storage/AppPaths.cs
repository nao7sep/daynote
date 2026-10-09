using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace DayNote.Core.Storage;

/// <summary>
/// Resolves the locations of the application's own files under <c>~/.daynote/</c>: configuration,
/// session state, records, and the backup store.
/// </summary>
/// <remarks>
/// The root is <c>DAYNOTE_DATA_DIR</c> when that environment variable is set and non-empty (the value is
/// expanded for a leading <c>~</c> and for environment references, then made absolute against the home
/// directory), otherwise the default <c>~/.daynote/</c>. The working directory is never a base for any
/// path, per the storage-path conventions. <c>DAYNOTE_DATA_DIR</c> is the one relocation seam, used the same
/// way by tests and in production.
/// </remarks>
public sealed class AppPaths
{
    /// <summary>Environment variable that relocates the entire storage root.</summary>
    public const string HomeEnvironmentVariable = "DAYNOTE_DATA_DIR";

    public AppPaths() => Root = ResolveRoot(Environment.GetEnvironmentVariable(HomeEnvironmentVariable), HomeDirectory());

    public string Root { get; }

    public string ConfigFile => Path.Combine(Root, "config.json");
    public string StateFile => Path.Combine(Root, "state.json");
    /// <summary>The records database (data-lifecycle conventions), opened by the app's logger.</summary>
    public string RecordsFile => Path.Combine(Root, "records.sqlite3");

    /// <summary>Where a log entry the records database could not take is written instead. Created by
    /// the first such write, never up front.</summary>
    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>
    /// The backup history, <c>backups.sqlite3</c>, directly under the root (see the data-backup
    /// conventions). Not created by <see cref="EnsureCreated"/>: the history opens itself lazily on the
    /// first recorded save, and its own <c>-wal</c>/<c>-shm</c>
    /// sidecars sit beside it — normal SQLite artifacts, not stray files.
    /// </summary>
    public string BackupStoreFile => Path.Combine(Root, "backups.sqlite3");

    /// <summary>Installs the warn sink used to report a failure to tighten the storage root's
    /// permissions to owner-only (<c>0700</c>) on POSIX. Optional: DayNote.Core stays logger-framework-
    /// free (see <see cref="DayNote.Core.Backup.BackupStore.ConfigureWarn"/> for the same pattern), so with
    /// no sink installed — such as during the very first <see cref="EnsureCreated"/> call at startup,
    /// before the app's logger exists — the failure goes to stderr instead. Either way, a tightening
    /// failure is reported, never thrown; it must never stop the app from starting.</summary>
    public static void ConfigureWarn(Action<string, Exception> warn) => _permissionWarn = warn;

    private static Action<string, Exception>? _permissionWarn;

    /// <summary>The permission mode the root must have on POSIX: owner read/write/execute, nothing else.</summary>
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Creates the root if it does not yet exist, and — on POSIX — makes
    /// sure the root itself (never its contents or subdirectories) is owner-only (<c>0700</c>): created
    /// that way from the start, and tightened when an existing root is broader. Windows uses its own
    /// permission model and skips both steps (storage-path conventions, "The resolver creates the
    /// root").</summary>
    public void EnsureCreated()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(Root);
        }
        else
        {
            // The UnixFileMode overload applies the mode only to a directory it creates, so a fresh root
            // is never briefly broader than owner-only; an existing root is tightened below.
            Directory.CreateDirectory(Root, OwnerOnly);
            EnsureOwnerOnly(Root);
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void EnsureOwnerOnly(string root)
    {
        var current = File.GetUnixFileMode(root);
        if ((current & ~OwnerOnly) != 0)
        {
            try
            {
                File.SetUnixFileMode(root, OwnerOnly);
            }
            catch (Exception ex)
            {
                var message = $"Could not tighten permissions on the storage root '{root}' to owner-only (0700).";
                if (_permissionWarn is { } warn)
                {
                    warn(message, ex);
                }
                else
                {
                    Console.Error.WriteLine("DayNote: " + message + " " + ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// The storage root for a <c>DAYNOTE_DATA_DIR</c> value (null or blank when unset) and a home
    /// directory: the override when one is given, otherwise <c>.daynote</c> under the home.
    /// </summary>
    public static string ResolveRoot(string? overrideValue, string home)
    {
        overrideValue = overrideValue?.Trim();
        if (!string.IsNullOrEmpty(overrideValue))
        {
            return ResolveOverride(overrideValue, home);
        }

        return Path.Combine(home, ".daynote");
    }

    // Matches `${VAR}`, `$VAR` (POSIX) and `%VAR%` (Windows) environment references.
    private static readonly Regex EnvReferencePattern = new(
        @"\$\{(?<braced>[A-Za-z_][A-Za-z0-9_]*)\}|\$(?<bare>[A-Za-z_][A-Za-z0-9_]*)|%(?<win>[A-Za-z_][A-Za-z0-9_]*)%",
        RegexOptions.Compiled);

    /// <summary>
    /// Expands <c>${VAR}</c> / <c>$VAR</c> / <c>%VAR%</c> references against the environment. An unset
    /// reference expands to empty — matching shell behavior and the TypeScript/Rust resolvers in the
    /// fleet — rather than being left as a literal that would become a directory name.
    /// </summary>
    private static string ExpandEnvReferences(string value) =>
        EnvReferencePattern.Replace(value, match =>
        {
            var name = match.Groups["braced"].Success ? match.Groups["braced"].Value
                : match.Groups["bare"].Success ? match.Groups["bare"].Value
                : match.Groups["win"].Value;
            return Environment.GetEnvironmentVariable(name) ?? string.Empty;
        });

    /// <summary>
    /// Expands environment references and a leading <c>~</c> in the override value, then makes it
    /// absolute against the home directory (never the working directory) so the override can never
    /// reintroduce a cwd dependence. An override that is set but expands to nothing (an unset
    /// <c>$VAR</c>/<c>%VAR%</c>) is a reported startup error, not a silent collapse onto the home directory.
    /// </summary>
    private static string ResolveOverride(string value, string home)
    {
        value = ExpandEnvReferences(value).Trim();

        if (value.Length == 0)
        {
            throw new InvalidOperationException(
                HomeEnvironmentVariable + " is set but expands to an empty path (an unset $VAR/%VAR%?). " +
                "Set it to a usable directory, or unset it to use the default.");
        }

        if (value == "~")
        {
            value = home;
        }
        else if (value.StartsWith("~/", StringComparison.Ordinal) ||
                 value.StartsWith("~" + Path.DirectorySeparatorChar))
        {
            value = Path.Combine(home, value[2..]);
        }

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
