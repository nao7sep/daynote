using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
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

    [Fact]
    public void Installer_Speaks_Inno_Setups_Own_Languages_With_English_First()
    {
        // Inno ships wizard text for eight of the ten interface languages; Korean and Simplified Chinese
        // readers get the English fallback, since no third-party translation is vendored. English
        // leads, so it is also what a computer in any other language sees.
        var (installer, _) = InstallerScript();
        var languages = Section(installer, "Languages")
            .Select(line => System.Text.RegularExpressions.Regex.Match(line, @"MessagesFile:\s*""([^""]+)""").Groups[1].Value)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "compiler:Default.isl",
                "compiler:Languages\\German.isl",
                "compiler:Languages\\Spanish.isl",
                "compiler:Languages\\French.isl",
                "compiler:Languages\\Italian.isl",
                "compiler:Languages\\BrazilianPortuguese.isl",
                "compiler:Languages\\Russian.isl",
                "compiler:Languages\\Japanese.isl",
            },
            languages);
    }

    [Fact]
    public void Installer_Words_Come_From_Innos_Translated_Messages()
    {
        // A literal Description would read in English under every wizard language; Inno's own custom
        // messages are translated in each of its language files.
        var (installer, _) = InstallerScript();
        foreach (var name in new[] { "Tasks", "Run" })
        {
            foreach (var line in Section(installer, name))
            {
                foreach (System.Text.RegularExpressions.Match value in System.Text.RegularExpressions.Regex.Matches(
                    line, @"(Description|GroupDescription):\s*""([^""]*)"""))
                {
                    Assert.StartsWith("{cm:", value.Groups[2].Value);
                }
            }
        }
    }

    [Fact]
    public void Mac_Bundle_Excludes_Debug_Symbols_Before_Signing()
    {
        var targets = BuildTargets();
        var publishedFiles = targets.Descendants("_PublishedFile").Single();
        var exclusions = ((string?)publishedFiles.Attribute("Exclude") ?? string.Empty).Split(';');

        Assert.Contains("$(PublishDir)**/*.pdb", exclusions);
    }

    [Fact]
    public void Application_License_Is_Packaged_On_Both_Platforms()
    {
        var targets = BuildTargets();
        var copy = targets.Descendants("Copy").Single(element =>
            (string?)element.Attribute("SourceFiles") == "$(MSBuildThisFileDirectory)LICENSE");
        Assert.Equal("$(_MacResDir)/LICENSE.txt", (string?)copy.Attribute("DestinationFiles"));
        Assert.Contains(
            "Copy-Item -LiteralPath LICENSE -Destination publish-win/LICENSE.txt",
            RepoFile("scripts", "package.ps1"));
    }

    private static XDocument BuildTargets([CallerFilePath] string callerPath = "")
    {
        var testsProjectDir = Path.GetDirectoryName(callerPath)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testsProjectDir, "..", ".."));
        return XDocument.Load(Path.Combine(repoRoot, "Directory.Build.targets"));
    }

    private static string RepoFile(string directory, string file, [CallerFilePath] string callerPath = "")
    {
        var testsProjectDir = Path.GetDirectoryName(callerPath)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testsProjectDir, "..", ".."));
        return File.ReadAllText(Path.Combine(repoRoot, directory, file));
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

    private static IEnumerable<string> Section(string text, string name)
    {
        var inSection = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inSection = line == $"[{name}]";
                continue;
            }

            if (inSection && line.Length > 0 && !line.StartsWith(';'))
            {
                yield return line;
            }
        }
    }
}
