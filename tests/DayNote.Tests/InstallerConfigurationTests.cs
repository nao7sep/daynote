using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace DayNote.Tests;

public sealed class InstallerConfigurationTests
{
    private static (string Text, string RepoRoot) InstallerScript([CallerFilePath] string callerPath = "")
    {
        var testsProjectDir = Path.GetDirectoryName(callerPath)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testsProjectDir, "..", ".."));
        return (File.ReadAllText(Path.Combine(repoRoot, "scripts", "daynote.iss")), repoRoot);
    }

    [Fact]
    public void Installer_Implements_The_Dual_Scope_Contract()
    {
        var (installer, repoRoot) = InstallerScript();
        var setup = KeyValueSection(installer, "Setup", '=');
        var run = KeyValueSection(installer, "Run", ':');
        var flags = run["Flags"].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        Assert.Equal("{#MyAppName}", setup["AppId"]);
        Assert.Equal("{autopf}\\{#MyAppName}", setup["DefaultDirName"]);
        Assert.Equal("dialog", setup["PrivilegesRequiredOverridesAllowed"]);
        Assert.False(setup.ContainsKey("PrivilegesRequired"));
        Assert.Equal("yes", setup["Uninstallable"]);
        var iconPath = setup["SetupIconFile"].Replace('\\', Path.DirectorySeparatorChar);
        Assert.True(File.Exists(Path.Combine(repoRoot, iconPath)), "The configured installer icon must exist.");
        Assert.Contains("runasoriginaluser", flags);
        Assert.DoesNotContain("runascurrentuser", flags);
        Assert.Equal("not IsAdminInstallMode", run["Check"]);
    }

    private static Dictionary<string, string> KeyValueSection(string text, string name, char separator)
    {
        var inSection = false;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals($"[{name}]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection || line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            foreach (var field in line.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var split = field.IndexOf(separator);
                if (split > 0)
                {
                    result[field[..split].Trim()] = field[(split + 1)..].Trim().Trim('"');
                }
            }
        }
        return result;
    }
}
